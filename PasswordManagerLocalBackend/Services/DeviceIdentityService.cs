using Microsoft.Extensions.DependencyInjection;
using NSec.Cryptography;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Security;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services;

public sealed class DeviceIdentityService : IDeviceIdentityService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private Key? _ka = null;
    private Key? _sig = null;
    private X509Certificate2? _cert = null;


    public DeviceIdentityService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }



    public bool IsInitialized =>
        _ka is not null && _sig is not null && _cert is not null;


    public byte[] AgreementPublicKey
    {
        get
        {
            if (_ka is null)
                throw new DeviceIdentityNotInitilaizedException();

            return _ka.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        }
    }

    public byte[] SignPublicKey
    {
        get
        {
            if (_sig is null)
                throw new DeviceIdentityNotInitilaizedException();

            return _sig.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        }
    }


    public string DeviceIdHex
    {
        get
        {
            var hash = Hashing.SHA256Hash(SignPublicKey);
            return Convert.ToHexString(hash);
        }
    }


    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        if (_sig is null)
            throw new DeviceIdentityNotInitilaizedException();

        return SignatureAlgorithm.Ed25519.Sign(_sig, data.ToArray());
    }


    public X509Certificate2 Certificate
    {
        get
        {
            if (_cert is null)
                throw new DeviceIdentityNotInitilaizedException();

            return _cert;
        }
    }


    public string FingerprintHex
    {
        get
        {
            if (_cert is null)
                throw new DeviceIdentityNotInitilaizedException();

            return GetFingerprintHex(_cert);
        }
    }



    public string GetFingerprintHex(X509Certificate2 cert)
    {
        var hash = Hashing.SHA256Hash(cert.RawData);
        return Convert.ToHexString(hash);
    }







    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (IsInitialized)
            return;

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDeviceIdentityRepository>();
        var keyProtector = scope.ServiceProvider.GetRequiredService<IKeyProtector>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var identity = await repo.Get(ct);
        if (identity is null)
            await CreateIdentity(repo, keyProtector, uow, ct);
        else
            LoadIdentity(identity, keyProtector);
    }



    private async Task CreateIdentity(IDeviceIdentityRepository repo, IKeyProtector keyProtector, IUnitOfWork uow, CancellationToken ct = default)
    {
        _ka = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        _sig = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        var (cert, pfxBytes) = CreateCertificateAndPfx();
        _cert = cert;

        var rawKa = _ka.Export(KeyBlobFormat.RawPrivateKey);
        var rawSig = _sig.Export(KeyBlobFormat.RawPrivateKey);

        try
        {
            var protectedKa = keyProtector.Protect(rawKa);
            var protectedSig = keyProtector.Protect(rawSig);
            var protectedCert = keyProtector.Protect(pfxBytes);

            var newIdentity = new LocalDeviceIdentity
            {
                AgreementPrivateKeyBlob = protectedKa,
                SignPrivateKeyBlob = protectedSig,
                PFXCertificate = protectedCert
            };
            newIdentity.GenerateIntegrityHash();

            await repo.Create(newIdentity, ct);
            await uow.SaveChangesAsync(ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKa);
            CryptographicOperations.ZeroMemory(rawSig);
            CryptographicOperations.ZeroMemory(pfxBytes);
        }
    }


    private void LoadIdentity(LocalDeviceIdentity identity, IKeyProtector keyProtector)
    {
        identity.VerifyIntegrity();

        var unprotectedKa = keyProtector.Unprotect(identity.AgreementPrivateKeyBlob);
        var unprotectedSig = keyProtector.Unprotect(identity.SignPrivateKeyBlob);
        var unprotectedCert = keyProtector.Unprotect(identity.PFXCertificate);

        try
        {
            _ka = Key.Import(KeyAgreementAlgorithm.X25519, unprotectedKa, KeyBlobFormat.RawPrivateKey);
            _sig = Key.Import(SignatureAlgorithm.Ed25519, unprotectedSig, KeyBlobFormat.RawPrivateKey);
            _cert = X509CertificateLoader.LoadPkcs12(unprotectedCert, PFXPassword, X509KeyStorageFlags.EphemeralKeySet, Pkcs12LoaderLimits.Defaults);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(unprotectedKa);
            CryptographicOperations.ZeroMemory(unprotectedSig);
            CryptographicOperations.ZeroMemory(unprotectedCert);
        }
    }



    private (X509Certificate2 Cert, byte[] PfxBytes) CreateCertificateAndPfx()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var req = new CertificateRequest("CN=PasswordManagerLocal Device", ecdsa, HashAlgorithmName.SHA256);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));

        try
        {
            var san = BuildSan();
            req.CertificateExtensions.Add(san.Build());
        }
        catch { }

        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection {
            new Oid("1.3.6.1.5.5.7.3.1"),
            new Oid("1.3.6.1.5.5.7.3.2")
            }, false));

        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(5);

        using var created = req.CreateSelfSigned(notBefore, notAfter);

        var pfx = created.Export(X509ContentType.Pfx, PFXPassword);

        var cert = X509CertificateLoader.LoadPkcs12(
            pfx,
            PFXPassword,
            X509KeyStorageFlags.EphemeralKeySet,
            Pkcs12LoaderLimits.Defaults);

        return (cert, pfx);
    }




    private SubjectAlternativeNameBuilder BuildSan()
    {
        var b = new SubjectAlternativeNameBuilder();
        try
        {
            b.AddDnsName(Dns.GetHostName());
        }
        catch { }

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var ipProps = ni.GetIPProperties();
                foreach (var ip in ipProps.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        b.AddIpAddress(ip.Address);
                }
            }
        }
        catch { }

        return b;
    }
}