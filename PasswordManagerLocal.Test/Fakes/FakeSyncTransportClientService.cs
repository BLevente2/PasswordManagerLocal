using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Sync;
using System.Collections.Concurrent;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeSyncTransportClientService : ISyncTransportClientService
{
    public bool SendResult { get; set; } = true;
    private int _sendCalls;
    public int SendCalls => Volatile.Read(ref _sendCalls);
    public string? LastHost { get; private set; }
    public int LastPort { get; private set; }
    public string? LastFingerprint { get; private set; }
    public IReadOnlyList<NetworkDelta> LastDeltas { get; private set; } = [];
    public TaskCompletionSource<bool>? SendGate { get; set; }
    public ConcurrentQueue<bool> SendResults { get; } = new();

    public async Task<bool> SendDeltasAsync(string host, int port, string serverFingerprintHex, IEnumerable<NetworkDelta> deltas, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _sendCalls);
        LastHost = host;
        LastPort = port;
        LastFingerprint = serverFingerprintHex;
        LastDeltas = deltas.ToList();

        if (SendGate is not null)
            await SendGate.Task.WaitAsync(ct);

        return SendResults.TryDequeue(out var queuedResult) ? queuedResult : SendResult;
    }


    public UserSnapshotInventoryExchangeReply InventoryReply { get; set; } = new();
    public IReadOnlyList<NetworkDelta> RequestedSnapshotDeltas { get; set; } = [];
    public int InventoryCalls { get; private set; }
    public int SnapshotRequestCalls { get; private set; }

    public Task<UserSnapshotInventoryExchangeReply> ExchangeUserSnapshotInventoryAsync(
        string host,
        int port,
        string serverFingerprintHex,
        UserSnapshotInventoryExchangeRequest request,
        CancellationToken ct = default)
    {
        InventoryCalls++;
        return Task.FromResult(InventoryReply);
    }

    public Task<IReadOnlyList<NetworkDelta>> RequestUserSnapshotsAsync(
        string host,
        int port,
        string serverFingerprintHex,
        UserSnapshotRequestBatch request,
        CancellationToken ct = default)
    {
        SnapshotRequestCalls++;
        return Task.FromResult(RequestedSnapshotDeltas);
    }

    public UserControlOperationInventoryExchangeReply ControlOperationInventoryReply { get; set; } = new();
    public IReadOnlyList<NetworkDelta> RequestedControlOperationDeltas { get; set; } = [];
    public int ControlOperationInventoryCalls { get; private set; }
    public int ControlOperationRequestCalls { get; private set; }

    public Task<UserControlOperationInventoryExchangeReply> ExchangeUserControlOperationInventoryAsync(
        string host,
        int port,
        string serverFingerprintHex,
        UserControlOperationInventoryExchangeRequest request,
        CancellationToken ct = default)
    {
        ControlOperationInventoryCalls++;
        return Task.FromResult(ControlOperationInventoryReply);
    }

    public Task<IReadOnlyList<NetworkDelta>> RequestUserControlOperationsAsync(
        string host,
        int port,
        string serverFingerprintHex,
        UserControlOperationRequestBatch request,
        CancellationToken ct = default)
    {
        ControlOperationRequestCalls++;
        return Task.FromResult(RequestedControlOperationDeltas);
    }

    public Task<GetDeviceEnrollmentInfoReply> GetDeviceEnrollmentInfoAsync(string host, int port, string serverFingerprintHex, GetDeviceEnrollmentInfoRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<CompleteDeviceEnrollmentReply> CompleteDeviceEnrollmentStreamAsync(string host, int port, string serverFingerprintHex, IAsyncEnumerable<CompleteDeviceEnrollmentChunk> chunks, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
