using PasswordManagerLocalBackend.Abstractions.Services;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeSyncRuntimeService : ISyncRuntimeService
{
    public int RefreshSyncEnabledCalls { get; private set; }
    public int BeginEnrollmentOnlyCalls { get; private set; }
    public int EndEnrollmentOnlyCalls { get; private set; }
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }

    public Task RefreshSyncEnabledAsync(CancellationToken ct = default)
    {
        RefreshSyncEnabledCalls++;
        return Task.CompletedTask;
    }

    public Task BeginEnrollmentOnlyAsync(CancellationToken ct = default)
    {
        BeginEnrollmentOnlyCalls++;
        return Task.CompletedTask;
    }

    public Task EndEnrollmentOnlyAsync(CancellationToken ct = default)
    {
        EndEnrollmentOnlyCalls++;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        StartCalls++;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        StopCalls++;
        return Task.CompletedTask;
    }
}
