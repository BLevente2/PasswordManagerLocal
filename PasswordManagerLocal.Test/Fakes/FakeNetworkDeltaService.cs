using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeNetworkDeltaService : INetworkDeltaService
{
    public NetworkDelta? LastApplied { get; private set; }
    public SyncItem? LastBuiltItem { get; private set; }
    public Device? LastBuiltDevice { get; private set; }
    public NetworkDeltaApplyResult ApplyResult { get; set; } = new(0);
    public NetworkDelta BuildResult { get; set; } = new();

    public Task<NetworkDeltaApplyResult> ApplyAsync(NetworkDelta delta, CancellationToken ct = default)
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
