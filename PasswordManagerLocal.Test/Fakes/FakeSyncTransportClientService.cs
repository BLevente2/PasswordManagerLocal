using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Sync;

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

    public async Task<bool> SendDeltasAsync(string host, int port, string serverFingerprintHex, IEnumerable<NetworkDelta> deltas, CancellationToken ct = default)
    {
        SendCalls++;
        LastHost = host;
        LastPort = port;
        LastFingerprint = serverFingerprintHex;
        LastDeltas = deltas.ToList();

        if (SendGate is not null)
            await SendGate.Task.WaitAsync(ct);

        return SendResult;
    }

    public Task<GetDeviceEnrollmentInfoReply> GetDeviceEnrollmentInfoAsync(string host, int port, string serverFingerprintHex, GetDeviceEnrollmentInfoRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<CompleteDeviceEnrollmentReply> CompleteDeviceEnrollmentStreamAsync(string host, int port, string serverFingerprintHex, IAsyncEnumerable<CompleteDeviceEnrollmentChunk> chunks, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
