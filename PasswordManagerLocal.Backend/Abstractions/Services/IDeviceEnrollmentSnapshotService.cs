using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceEnrollmentSnapshotService
{
    Task<DeviceEnrollmentSnapshot> BuildAsync(IServiceProvider services, Guid userId, CancellationToken ct = default);
    Task EnsureEncryptedDeviceDataAsync(IUserService users, User user, Guid token, Guid deviceId, CancellationToken ct = default);
    (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(
        string sessionId,
        byte[] secret,
        byte[] plaintext,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsFingerprint);
    DeviceEnrollmentSnapshot DecryptAndDeserialize(
        string sessionId,
        byte[] secret,
        byte[] ciphertext,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsFingerprint,
        int encryptionVersion,
        byte[] nonce,
        byte[] tag);
}
