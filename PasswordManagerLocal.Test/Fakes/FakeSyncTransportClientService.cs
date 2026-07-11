using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Sync;
using System.Collections.Concurrent;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeSyncTransportClientService : ISyncTransportClientService
{
    public bool SendResult { get; set; } = true;
    public int SendCalls { get; private set; }
    public string? LastHost { get; private set; }
    public int LastPort { get; private set; }
    public string? LastFingerprint { get; private set; }
    public IReadOnlyList<NetworkDelta> LastDeltas { get; private set; } = [];
    public TaskCompletionSource<bool>? SendGate { get; set; }
    public ConcurrentQueue<bool> SendResults { get; } = new();

    public async Task<bool> SendDeltasAsync(string host, int port, string serverFingerprintHex, IEnumerable<NetworkDelta> deltas, CancellationToken ct = default)
    {
        SendCalls++;
        LastHost = host;
        LastPort = port;
        LastFingerprint = serverFingerprintHex;
        LastDeltas = deltas.ToList();

        if (SendGate is not null)
            await SendGate.Task.WaitAsync(ct);

        return SendResults.TryDequeue(out var queuedResult) ? queuedResult : SendResult;
    }

    public Task<GetDeviceEnrollmentInfoReply> GetDeviceEnrollmentInfoAsync(string host, int port, string serverFingerprintHex, GetDeviceEnrollmentInfoRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<CompleteDeviceEnrollmentReply> CompleteDeviceEnrollmentStreamAsync(string host, int port, string serverFingerprintHex, IAsyncEnumerable<CompleteDeviceEnrollmentChunk> chunks, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
