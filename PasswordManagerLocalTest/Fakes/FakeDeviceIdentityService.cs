using PasswordManagerLocalBackend.Abstractions.Services;
using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeDeviceIdentityService : IDeviceIdentityService
{
    public bool IsInitialized { get; set; }
    public bool IsSyncOn { get; private set; } = true;
    public string DeviceName { get; private set; } = "Test device";
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public byte[] AgreementPublicKey { get; } = [];
    public byte[] SignPublicKey { get; } = [];
    public Guid LocalDeviceId { get; set; } = Guid.Empty;
    public string DeviceIdHex => LocalDeviceId.ToString("N").ToUpperInvariant();
    public X509Certificate2 Certificate => throw new NotSupportedException("The fake device identity has no certificate.");
    public string FingerprintHex { get; set; } = string.Empty;

    public Task InitializeAsync(CancellationToken ct = default)
    {
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public Task SetSyncOnAsync(bool isSyncOn, CancellationToken ct = default)
    {
        IsSyncOn = isSyncOn;
        return Task.CompletedTask;
    }

    public Task SetDeviceNameAsync(string deviceName, CancellationToken ct = default)
    {
        DeviceName = deviceName;
        return Task.CompletedTask;
    }

    public byte[] Sign(ReadOnlySpan<byte> data) => [];

    public byte[] EncryptForDevice(byte[] plaintext, byte[] recipientAgreementPublicKey, byte[] associatedData, out byte[] ephemeralPublicKey, out byte[] nonce, out byte[] tag)
    {
        ephemeralPublicKey = [];
        nonce = [];
        tag = [];
        return plaintext.ToArray();
    }

    public byte[] DecryptFromDevice(byte[] ciphertext, byte[] senderEphemeralPublicKey, byte[] nonce, byte[] tag, byte[] associatedData) =>
        ciphertext.ToArray();

    public string GetFingerprintHex(X509Certificate2 cert) => FingerprintHex;
}
