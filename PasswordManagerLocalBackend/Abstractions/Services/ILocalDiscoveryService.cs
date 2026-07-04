using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Sync.Enrollment;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ILocalDiscoveryService
{
    void ActivateEnrollmentSession(string sessionId, byte[] secret, DateTimeOffset expiresAt);
    void DeactivateEnrollmentSession(string sessionId);
    Task<IReadOnlyList<EnrollmentEndpoint>> FindEnrollmentEndpointsAsync(DeviceEnrollmentParsedCode parsed, CancellationToken ct = default);
}
