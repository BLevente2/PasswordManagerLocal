using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeDeviceIdentityService : IDeviceIdentityService
{
    public bool IsInitialized { get; set; } = true;
    public bool IsSyncOn { get; set; } = true;
    public int SetSyncOnCalls { get; private set; }
    public DeviceType DeviceType { get; set; } = PasswordManagerLocalBackend.Models.DeviceType.WindowsPc;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[] AgreementPublicKey { get; set; } = [];
    public byte[] SignPublicKey { get; set; } = [];
    public Guid LocalDeviceId { get; set; } = Guid.Parse("A46D349F-8C54-4424-B974-2E51D113EBE8");
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
        SetSyncOnCalls++;
        IsSyncOn = isSyncOn;
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
