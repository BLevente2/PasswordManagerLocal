using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Abstractions.Sync.Discovery;
using PasswordManagerLocal.Backend.Configuration;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

namespace PasswordManagerLocal.Test.Backend.Hosting;

[TestClass]
public sealed class BackendRuntimeTests
{
    [TestMethod]
    public void InitialSnapshot_IsNotStarted()
    {
        using var directory = new TemporaryDirectory();
        var runtime = CreateRuntime(directory.Path, _ => Task.CompletedTask, new FakeSyncRuntimeService(), out _);

        Assert.AreEqual(BackendRuntimeState.NotStarted, runtime.Snapshot.State);
        Assert.AreEqual(BackendRuntimeFailureKind.None, runtime.Snapshot.FailureKind);
    }

    [TestMethod]
    public async Task ConcurrentEnsureStarted_CreatesOneHost()
    {
        using var directory = new TemporaryDirectory();
        var releaseInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = CreateRuntime(
            directory.Path,
            _ => releaseInitialization.Task,
            new FakeSyncRuntimeService(),
            out var hostCreations);

        var starts = Enumerable.Range(0, 10)
            .Select(_ => runtime.EnsureStartedAsync())
            .ToArray();

        await WaitForAsync(() => hostCreations() == 1);
        releaseInitialization.SetResult();
        await Task.WhenAll(starts);

        Assert.AreEqual(1, hostCreations());
        Assert.AreEqual(BackendRuntimeState.Ready, runtime.Snapshot.State);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task CallerCancellation_DoesNotCancelSharedStartup()
    {
        using var directory = new TemporaryDirectory();
        var releaseInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = CreateRuntime(
            directory.Path,
            _ => releaseInitialization.Task,
            new FakeSyncRuntimeService(),
            out _);
        using var cancellation = new CancellationTokenSource();

        var cancelledWaiter = runtime.EnsureStartedAsync(cancellation.Token);
        var survivingWaiter = runtime.EnsureStartedAsync();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await cancelledWaiter);
        releaseInitialization.SetResult();
        await survivingWaiter;

        Assert.AreEqual(BackendRuntimeState.Ready, runtime.Snapshot.State);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task FailedStartup_CanBeRetriedWithANewHost()
    {
        using var directory = new TemporaryDirectory();
        var attempts = 0;
        var runtime = CreateRuntime(
            directory.Path,
            _ => ++attempts == 1
                ? Task.FromException(new InvalidOperationException("first failure"))
                : Task.CompletedTask,
            new FakeSyncRuntimeService(),
            out var hostCreations);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runtime.EnsureStartedAsync());
        Assert.AreEqual(BackendRuntimeState.Failed, runtime.Snapshot.State);

        await runtime.EnsureStartedAsync();

        Assert.AreEqual(2, hostCreations());
        Assert.AreEqual(BackendRuntimeState.Ready, runtime.Snapshot.State);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task DeviceLockedFailure_TransitionsToWaitingAndCanRetry()
    {
        using var directory = new TemporaryDirectory();
        var keyAvailable = false;
        var hostCreations = 0;
        var paths = new BackendStoragePaths(directory.Path);
        var options = new BackendRuntimeOptions(
            paths,
            () => keyAvailable
                ? new TestKeyProtector()
                : throw new KeyProtectorUnavailableException(KeyProtectorUnavailableReason.DeviceLocked),
            static () => new FakeLocalDiscoveryNetworkLease(),
            (_, _) =>
            {
                hostCreations++;
                return CreateHost(new DelegateInitializationService(_ => Task.CompletedTask), new FakeSyncRuntimeService());
            });
        var runtime = new BackendRuntime(options);

        await Assert.ThrowsAsync<KeyProtectorUnavailableException>(async () => await runtime.EnsureStartedAsync());
        Assert.AreEqual(BackendRuntimeState.WaitingForDeviceUnlock, runtime.Snapshot.State);

        keyAvailable = true;
        await runtime.EnsureStartedAsync();

        Assert.AreEqual(1, hostCreations);
        Assert.AreEqual(BackendRuntimeState.Ready, runtime.Snapshot.State);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task SyncFailure_LeavesBackendReadyAndMarksSyncDegraded()
    {
        using var directory = new TemporaryDirectory();
        var syncFailure = new InvalidOperationException("sync failed");
        var syncRuntime = new FakeSyncRuntimeService { RefreshFailure = syncFailure };
        var runtime = CreateRuntime(directory.Path, _ => Task.CompletedTask, syncRuntime, out _);

        await runtime.EnsureStartedAsync();
        await WaitForAsync(() => runtime.SyncSnapshot.State == SyncRuntimeState.Degraded);

        Assert.AreEqual(BackendRuntimeState.Ready, runtime.Snapshot.State);
        Assert.AreSame(syncFailure, runtime.SyncSnapshot.Failure);
        Assert.IsNotNull(await runtime.GetEndpointsAsync());
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task GetEndpoints_ReturnsSingletonOnlyAfterReadiness()
    {
        using var directory = new TemporaryDirectory();
        var releaseInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = CreateRuntime(
            directory.Path,
            _ => releaseInitialization.Task,
            new FakeSyncRuntimeService(),
            out _);

        var endpointsTask = runtime.GetEndpointsAsync();
        Assert.IsFalse(endpointsTask.IsCompleted);
        releaseInitialization.SetResult();

        var first = await endpointsTask;
        var second = await runtime.GetEndpointsAsync();
        Assert.AreSame(first, second);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task ResetAfterCompatibilityFailure_DeletesStorageAndRestarts()
    {
        using var directory = new TemporaryDirectory();
        var attempts = 0;
        var runtime = CreateRuntime(
            directory.Path,
            _ => ++attempts == 1
                ? Task.FromException(new DatabaseVersionNotSupportedException(99, 1, 1))
                : Task.CompletedTask,
            new FakeSyncRuntimeService(),
            out _);

        await Assert.ThrowsAsync<DatabaseVersionNotSupportedException>(async () => await runtime.EnsureStartedAsync());

        var paths = new BackendStoragePaths(directory.Path);
        foreach (var path in new[]
                 {
                     paths.DatabasePath,
                     paths.DatabaseWalPath,
                     paths.DatabaseShmPath,
                     paths.DatabaseJournalPath,
                     paths.DatabaseConfigPath,
                     $"{paths.DatabaseConfigPath}.123.tmp"
                 })
        {
            await File.WriteAllTextAsync(path, "test");
        }

        await runtime.ResetDatabaseAndRestartAsync();

        Assert.AreEqual(BackendRuntimeState.Ready, runtime.Snapshot.State);
        Assert.IsFalse(File.Exists(paths.DatabasePath));
        Assert.IsFalse(File.Exists(paths.DatabaseWalPath));
        Assert.IsFalse(File.Exists(paths.DatabaseShmPath));
        Assert.IsFalse(File.Exists(paths.DatabaseJournalPath));
        Assert.IsFalse(File.Exists(paths.DatabaseConfigPath));
        Assert.IsFalse(File.Exists($"{paths.DatabaseConfigPath}.123.tmp"));
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task SuccessfulStartup_RaisesStartingThenReady()
    {
        using var directory = new TemporaryDirectory();
        var runtime = CreateRuntime(
            directory.Path,
            _ => Task.CompletedTask,
            new FakeSyncRuntimeService(),
            out _);
        var states = new List<BackendRuntimeState>();
        runtime.StateChanged += (_, args) => states.Add(args.Current.State);

        await runtime.EnsureStartedAsync();

        CollectionAssert.AreEqual(
            new[] { BackendRuntimeState.Starting, BackendRuntimeState.Ready },
            states);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task ThrowingStateObserver_DoesNotBreakStartup()
    {
        using var directory = new TemporaryDirectory();
        var runtime = CreateRuntime(
            directory.Path,
            _ => Task.CompletedTask,
            new FakeSyncRuntimeService(),
            out _);
        runtime.StateChanged += (_, _) => throw new InvalidOperationException("observer failure");

        await runtime.EnsureStartedAsync();

        Assert.AreEqual(BackendRuntimeState.Ready, runtime.Snapshot.State);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task StopThenStart_CreatesANewHost()
    {
        using var directory = new TemporaryDirectory();
        var runtime = CreateRuntime(
            directory.Path,
            _ => Task.CompletedTask,
            new FakeSyncRuntimeService(),
            out var hostCreations);

        await runtime.EnsureStartedAsync();
        await runtime.StopAsync();
        await runtime.EnsureStartedAsync();

        Assert.AreEqual(2, hostCreations());
        Assert.AreEqual(BackendRuntimeState.Ready, runtime.Snapshot.State);
        await runtime.DisposeAsync();
    }

    [TestMethod]
    public async Task ResetWhileHealthy_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var runtime = CreateRuntime(
            directory.Path,
            _ => Task.CompletedTask,
            new FakeSyncRuntimeService(),
            out _);
        await runtime.EnsureStartedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runtime.ResetDatabaseAndRestartAsync());

        await runtime.DisposeAsync();
    }

    [TestMethod]
    public void RuntimeCreation_RejectsNullRequiredDependencies()
    {
        using var directory = new TemporaryDirectory();
        var paths = new BackendStoragePaths(directory.Path);

        Assert.Throws<ArgumentNullException>(() => BackendRuntimeFactory.Create(
            null!,
            static () => new TestKeyProtector(),
            static () => new FakeLocalDiscoveryNetworkLease()));
        Assert.Throws<ArgumentNullException>(() => BackendRuntimeFactory.Create(
            paths,
            null!,
            static () => new FakeLocalDiscoveryNetworkLease()));
        Assert.Throws<ArgumentNullException>(() => BackendRuntimeFactory.Create(
            paths,
            static () => new TestKeyProtector(),
            null!));
    }

    [TestMethod]
    public async Task FactoryReturningNull_FailsStartup()
    {
        using var directory = new TemporaryDirectory();
        var runtime = BackendRuntimeFactory.Create(
            new BackendStoragePaths(directory.Path),
            static () => null!,
            static () => new FakeLocalDiscoveryNetworkLease());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runtime.EnsureStartedAsync());

        Assert.AreEqual(BackendRuntimeState.Failed, runtime.Snapshot.State);
        await runtime.DisposeAsync();
    }

    private static BackendRuntime CreateRuntime(
        string directory,
        Func<CancellationToken, Task> initialize,
        FakeSyncRuntimeService syncRuntime,
        out Func<int> hostCreationCount)
    {
        var hostCreations = 0;
        var options = new BackendRuntimeOptions(
            new BackendStoragePaths(directory),
            static () => new TestKeyProtector(),
            static () => new FakeLocalDiscoveryNetworkLease(),
            (_, _) =>
            {
                Interlocked.Increment(ref hostCreations);
                return CreateHost(new DelegateInitializationService(initialize), syncRuntime);
            });
        hostCreationCount = () => Volatile.Read(ref hostCreations);
        return new BackendRuntime(options);
    }

    private static BackendServiceHost CreateHost(
        IBackendInitializationService initialization,
        ISyncRuntimeService syncRuntime)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBackendInitializationService>(initialization);
        services.AddSingleton<ISyncRuntimeService>(syncRuntime);
        services.AddSingleton<IEndpoints, Endpoints>();
        return new BackendServiceHost(services.BuildServiceProvider());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= timeout)
                Assert.Fail("The expected state was not reached.");

            await Task.Delay(10);
        }
    }

    private sealed class DelegateInitializationService : IBackendInitializationService
    {
        private readonly Func<CancellationToken, Task> _initialize;

        public DelegateInitializationService(Func<CancellationToken, Task> initialize)
        {
            _initialize = initialize;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            _initialize(cancellationToken);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PasswordManagerLocal.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
