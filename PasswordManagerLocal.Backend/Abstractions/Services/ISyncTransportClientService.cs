using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ISyncTransportClientService
{
    Task<bool> SendDeltasAsync(string host, int port, string serverFingerprintHex, IEnumerable<NetworkDelta> deltas, CancellationToken ct = default);

    Task<UserSnapshotInventoryExchangeReply> ExchangeUserSnapshotInventoryAsync(
        string host,
        int port,
        string serverFingerprintHex,
        UserSnapshotInventoryExchangeRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<NetworkDelta>> RequestUserSnapshotsAsync(
        string host,
        int port,
        string serverFingerprintHex,
        UserSnapshotRequestBatch request,
        CancellationToken ct = default);

    Task<GetDeviceEnrollmentInfoReply> GetDeviceEnrollmentInfoAsync(string host, int port, string serverFingerprintHex, GetDeviceEnrollmentInfoRequest request, CancellationToken ct = default);

    Task<CompleteDeviceEnrollmentReply> CompleteDeviceEnrollmentStreamAsync(string host, int port, string serverFingerprintHex, IAsyncEnumerable<CompleteDeviceEnrollmentChunk> chunks, CancellationToken ct = default);
}
