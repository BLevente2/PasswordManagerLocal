using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IDeviceIdentityService
{
    Task InitializeAsync(CancellationToken ct = default);
    bool IsInitialized { get; }
    byte[] AgreementPublicKey { get; }
    byte[] SignPublicKey { get; }
    string DeviceIdHex { get; }
    byte[] Sign(ReadOnlySpan<byte> data);

    X509Certificate2 Certificate { get; }
    string FingerprintHex { get; }
    string GetFingerprintHex(X509Certificate2 cert);
}