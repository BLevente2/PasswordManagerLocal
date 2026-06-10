using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IDeviceEnrollmentService
{
    Task<DeviceEnrollmentCodeResponse> StartEnrollmentAsync(CancellationToken ct = default);
    Task<DeviceEnrollmentStatusResponse> GetEnrollmentStatusAsync(CancellationToken ct = default);
    Task CancelEnrollmentAsync(CancellationToken ct = default);
    Task AddDeviceByCodeAsync(Guid token, string code, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> CompleteIncomingEnrollmentAsync(string sessionId, byte[] codeProof, byte[] snapshot, string sourceDeviceId, byte[] sourceSignPublicKey, string sourceTlsCertFingerprint, string actualClientTlsCertFingerprint, CancellationToken ct = default);
}
