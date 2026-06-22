using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeNetworkDeltaService : INetworkDeltaService
{
    public NetworkDelta? LastApplied { get; private set; }
    public SyncItem? LastBuiltItem { get; private set; }
    public Device? LastBuiltDevice { get; private set; }
    public long ApplyResult { get; set; }
    public NetworkDelta BuildResult { get; set; } = new();

    public Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default)
    {
        LastApplied = delta;
        return Task.FromResult(ApplyResult);
    }

    public Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default)
    {
        LastBuiltItem = item;
        LastBuiltDevice = device;
        return Task.FromResult(BuildResult);
    }
}
