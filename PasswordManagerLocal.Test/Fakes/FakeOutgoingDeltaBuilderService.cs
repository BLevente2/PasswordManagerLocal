using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeOutgoingDeltaBuilderService : IOutgoingDeltaBuilderService
{
    public NetworkDelta Result { get; set; } = new();
    public SyncItem? LastItem { get; private set; }
    public Device? LastDevice { get; private set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default)
    {
        LastItem = item;
        LastDevice = device;
        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return Task.FromResult(Result);
    }
}
