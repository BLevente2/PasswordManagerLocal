using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IGrpcClientService
{
    Task<bool> SendAsync(string host, int port, string serverFingerprintHex, IEnumerable<NetworkDelta> deltas, CancellationToken ct = default);
}