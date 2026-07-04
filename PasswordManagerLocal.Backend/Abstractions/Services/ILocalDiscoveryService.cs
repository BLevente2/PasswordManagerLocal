using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Enrollment;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ILocalDiscoveryService
{
    void ActivateEnrollmentSession(string sessionId, byte[] secret, DateTimeOffset expiresAt);
    void DeactivateEnrollmentSession(string sessionId);
    Task<IReadOnlyList<EnrollmentEndpoint>> FindEnrollmentEndpointsAsync(DeviceEnrollmentParsedCode parsed, CancellationToken ct = default);
}
