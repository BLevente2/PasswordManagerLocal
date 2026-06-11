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
using static PasswordManagerLocalBackend.Utils.DataValidationUtil;
using PasswordManagerLocalBackend.Utils;

namespace PasswordManagerLocalBackend.Services;

public sealed class DeviceIdentityService : IDeviceIdentityService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private Key? _ka = null;
    private Key? _sig = null;
    private X509Certificate2? _cert = null;
    private Guid _localDeviceId = Guid.Empty;
    private string _deviceName = string.Empty;
    private bool _isSyncOn = true;
    private DateTimeOffset _createdAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);


    public DeviceIdentityService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }



    public bool IsInitialized =>
        _ka is not null && _sig is not null && _cert is not null;


    public bool IsSyncOn =>
        _isSyncOn;


    public string DeviceName =>
        _deviceName;


    public DateTimeOffset CreatedAt =>
        _createdAt;


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

    public Guid LocalDeviceId
    {
        get
        {
            if (_localDeviceId == Guid.Empty)
                throw new DeviceIdentityNotInitilaizedException();

            return _localDeviceId;
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


    public byte[] EncryptForDevice(byte[] plaintext, byte[] recipientAgreementPublicKey, byte[] associatedData, out byte[] ephemeralPublicKey, out byte[] nonce, out byte[] tag)
    {
        if (plaintext is null || plaintext.Length == 0)
            throw new InvalidDataException("Plaintext payload is empty.");

        if (recipientAgreementPublicKey is null || recipientAgreementPublicKey.Length != SyncDeltaX25519PublicKeyBytes)
            throw new InvalidDataException("Recipient agreement public key is invalid.");

        using var ephemeralKey = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters());
        var recipientPublicKey = NSec.Cryptography.PublicKey.Import(KeyAgreementAlgorithm.X25519, recipientAgreementPublicKey, KeyBlobFormat.RawPublicKey);
        using var sharedSecret = KeyAgreementAlgorithm.X25519.Agree(
            ephemeralKey,
            recipientPublicKey,
            new SharedSecretCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport })
            ?? throw new CryptographicException("Key agreement failed.");

        ephemeralPublicKey = ephemeralKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        nonce = RandomNumberGenerator.GetBytes(SyncDeltaNonceBytes);
        tag = new byte[SyncDeltaTagBytes];

        var key = DeriveSyncAesKey(sharedSecret, ephemeralPublicKey, recipientAgreementPublicKey, associatedData);
        try
        {
            var ciphertext = new byte[plaintext.Length];
            using var aes = new AesGcm(key, SyncDeltaTagBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return ciphertext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }


    public byte[] DecryptFromDevice(byte[] ciphertext, byte[] senderEphemeralPublicKey, byte[] nonce, byte[] tag, byte[] associatedData)
    {
        if (_ka is null)
            throw new DeviceIdentityNotInitilaizedException();

        if (ciphertext is null || ciphertext.Length == 0)
            throw new InvalidDataException("Ciphertext payload is empty.");

        if (senderEphemeralPublicKey is null || senderEphemeralPublicKey.Length != SyncDeltaX25519PublicKeyBytes)
            throw new InvalidDataException("Sender ephemeral public key is invalid.");

        if (nonce is null || nonce.Length != SyncDeltaNonceBytes)
            throw new InvalidDataException("Delta nonce is invalid.");

        if (tag is null || tag.Length != SyncDeltaTagBytes)
            throw new InvalidDataException("Delta authentication tag is invalid.");

        var senderPublicKey = NSec.Cryptography.PublicKey.Import(KeyAgreementAlgorithm.X25519, senderEphemeralPublicKey, KeyBlobFormat.RawPublicKey);
        using var sharedSecret = KeyAgreementAlgorithm.X25519.Agree(
            _ka,
            senderPublicKey,
            new SharedSecretCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport })
            ?? throw new CryptographicException("Key agreement failed.");

        var key = DeriveSyncAesKey(sharedSecret, senderEphemeralPublicKey, AgreementPublicKey, associatedData);
        try
        {
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, SyncDeltaTagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }


    private static byte[] DeriveSyncAesKey(SharedSecret sharedSecret, byte[] firstPublicKey, byte[] secondPublicKey, byte[] associatedData)
    {
        var rawSharedSecret = sharedSecret.Export(SharedSecretBlobFormat.RawSharedSecret);
        try
        {
            var salt = BuildKdfSalt(firstPublicKey, secondPublicKey);
            var info = BuildKdfInfo(firstPublicKey, secondPublicKey, associatedData);
            return HKDF.DeriveKey(HashAlgorithmName.SHA512, rawSharedSecret, 32, salt, info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawSharedSecret);
        }
    }


    private static byte[] BuildKdfSalt(byte[] firstPublicKey, byte[] secondPublicKey)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write("PasswordManagerLocalBackend.SyncDelta.KdfSalt.v1");
        bw.Write(firstPublicKey.Length);
        bw.Write(firstPublicKey);
        bw.Write(secondPublicKey.Length);
        bw.Write(secondPublicKey);

        return Hashing.SHA256Hash(ms.ToArray());
    }


    private static byte[] BuildKdfInfo(byte[] firstPublicKey, byte[] secondPublicKey, byte[] associatedData)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write("PasswordManagerLocalBackend.SyncDelta.AES256GCM.v1");
        bw.Write(firstPublicKey.Length);
        bw.Write(firstPublicKey);
        bw.Write(secondPublicKey.Length);
        bw.Write(secondPublicKey);
        bw.Write(associatedData.Length);
        bw.Write(associatedData);

        return ms.ToArray();
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

        await _initializationLock.WaitAsync(ct);
        try
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
            {
                await UpgradeLegacyIdentityIfNeeded(identity, repo, uow, ct);
                LoadIdentity(identity, keyProtector);
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }


    public async Task SetSyncOnAsync(bool isSyncOn, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDeviceIdentityRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var identity = await repo.Get(ct);
        if (identity is null)
            throw new DeviceIdentityNotInitilaizedException();

        identity.VerifyIntegrity();

        if (identity.IsSyncOn == isSyncOn && _isSyncOn == isSyncOn)
            return;

        identity.IsSyncOn = isSyncOn;
        identity.GenerateIntegrityHash();
        repo.Update(identity);
        await uow.SaveChangesAsync(ct);

        _isSyncOn = isSyncOn;
    }

    public async Task SetDeviceNameAsync(string deviceName, CancellationToken ct = default)
    {
        var normalizedName = NormalizeDeviceName(deviceName);
        await InitializeAsync(ct);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDeviceIdentityRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var identity = await repo.Get(ct);
        if (identity is null)
            throw new DeviceIdentityNotInitilaizedException();

        identity.VerifyIntegrity();

        if (string.Equals(identity.DeviceName, normalizedName, StringComparison.Ordinal) &&
            string.Equals(_deviceName, normalizedName, StringComparison.Ordinal))
            return;

        identity.DeviceName = normalizedName;
        identity.GenerateIntegrityHash();
        repo.Update(identity);
        await uow.SaveChangesAsync(ct);

        _deviceName = normalizedName;
    }




    private async Task UpgradeLegacyIdentityIfNeeded(LocalDeviceIdentity identity, IDeviceIdentityRepository repo, IUnitOfWork uow, CancellationToken ct = default)
    {
        if (identity.IsIntegrityValid())
        {
            if (string.IsNullOrWhiteSpace(identity.DeviceName))
            {
                identity.DeviceName = DeviceNameUtil.BuildDefaultDeviceName(identity.Id);
                identity.GenerateIntegrityHash();
                repo.Update(identity);
                await uow.SaveChangesAsync(ct);
            }

            return;
        }

        var legacyHash = identity.CalculateLegacyIntegrityHashWithoutDeviceName();
        if (Hashing.Verify(identity.IntegrityHash, legacyHash))
        {
            identity.DeviceName = DeviceNameUtil.BuildDefaultDeviceName(identity.Id);
            identity.GenerateIntegrityHash();
            repo.Update(identity);
            await uow.SaveChangesAsync(ct);
            return;
        }

        legacyHash = identity.CalculateLegacyIntegrityHashWithoutSyncFlagAndDeviceName();
        if (!Hashing.Verify(identity.IntegrityHash, legacyHash))
        {
            identity.VerifyIntegrity();
            return;
        }

        identity.DeviceName = DeviceNameUtil.BuildDefaultDeviceName(identity.Id);
        identity.IsSyncOn = true;
        identity.GenerateIntegrityHash();
        repo.Update(identity);
        await uow.SaveChangesAsync(ct);
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
            var newIdentityId = Guid.NewGuid();

            var newIdentity = new LocalDeviceIdentity
            {
                Id = newIdentityId,
                AgreementPrivateKeyBlob = protectedKa,
                SignPrivateKeyBlob = protectedSig,
                PFXCertificate = protectedCert,
                DeviceName = DeviceNameUtil.BuildDefaultDeviceName(newIdentityId),
                IsSyncOn = true
            };
            _localDeviceId = newIdentity.Id;
            _deviceName = newIdentity.DeviceName;
            _isSyncOn = true;
            _createdAt = newIdentity.CreatedAt;
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
        _localDeviceId = identity.Id;
        _deviceName = string.IsNullOrWhiteSpace(identity.DeviceName) ? DeviceNameUtil.BuildDefaultDeviceName(identity.Id) : identity.DeviceName;
        _isSyncOn = identity.IsSyncOn;
        _createdAt = identity.CreatedAt;

        var unprotectedKa = keyProtector.Unprotect(identity.AgreementPrivateKeyBlob);
        var unprotectedSig = keyProtector.Unprotect(identity.SignPrivateKeyBlob);
        var unprotectedCert = keyProtector.Unprotect(identity.PFXCertificate);

        try
        {
            _ka = Key.Import(KeyAgreementAlgorithm.X25519, unprotectedKa, KeyBlobFormat.RawPrivateKey);
            _sig = Key.Import(SignatureAlgorithm.Ed25519, unprotectedSig, KeyBlobFormat.RawPrivateKey);
            _cert = X509CertificateLoader.LoadPkcs12(unprotectedCert, PFXPassword, GetCertificateKeyStorageFlags(), Pkcs12LoaderLimits.Defaults);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(unprotectedKa);
            CryptographicOperations.ZeroMemory(unprotectedSig);
            CryptographicOperations.ZeroMemory(unprotectedCert);
        }
    }



    private static string NormalizeDeviceName(string name)
    {
        if (!IsValidUserDeviceName(name))
            throw new InvalidInputException();

        return name.Trim();
    }



    private static X509KeyStorageFlags GetCertificateKeyStorageFlags() =>
        OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.EphemeralKeySet;



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
            GetCertificateKeyStorageFlags(),
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