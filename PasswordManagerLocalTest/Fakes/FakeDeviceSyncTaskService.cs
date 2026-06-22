using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalTest.Fakes;

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
