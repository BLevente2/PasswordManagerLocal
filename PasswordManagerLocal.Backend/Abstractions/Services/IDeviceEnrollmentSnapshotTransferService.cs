using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Enrollment;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceEnrollmentSnapshotTransferService
{
    Task<(bool Ok, DeviceEnrollmentErrorCode ErrorCode, string? Error)> SendAsync(
        EnrollmentEndpoint endpoint,
        string sessionId,
        byte[] secret,
        byte[] proof,
        DeviceEnrollmentSnapshot snapshot,
        CancellationToken ct = default);
}
