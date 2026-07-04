using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface INetworkDeltaProtocolService
{
    Task<ValidatedNetworkDelta> ValidateAndReadAsync(NetworkDelta delta, CancellationToken ct = default);
    Task ValidateSourceAuthorizationAsync(Device sourceDevice, SyncDeltaPayload payload, CancellationToken ct = default);
}
