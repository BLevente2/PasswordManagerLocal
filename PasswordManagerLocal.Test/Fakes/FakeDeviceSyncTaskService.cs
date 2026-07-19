using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Discovery;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeDeviceSyncTaskService : IDeviceSyncTaskService
{
    public List<(DiscoveredDeviceEndpoint Endpoint, Device Device)> Starts { get; } = [];
    public int StopAllCalls { get; private set; }

    public bool TryStart(DiscoveredDeviceEndpoint endpoint, Device device)
    {
        Starts.Add((endpoint, device));
        return true;
    }

    public Task StopAllAsync(CancellationToken ct = default)
    {
        StopAllCalls++;
        return Task.CompletedTask;
    }
}
