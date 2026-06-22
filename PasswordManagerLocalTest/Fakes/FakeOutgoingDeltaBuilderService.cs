using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Sync;

namespace PasswordManagerLocalTest.Fakes;

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
