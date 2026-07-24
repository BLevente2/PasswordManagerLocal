using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Android.Runtime;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

namespace PasswordManagerLocal.Test.Android.Runtime;

[TestClass]
public sealed class AndroidRuntimeServiceHostTests
{

    [TestMethod]
    public async Task DisabledRestorationStopsStartedServiceWithoutCreatingRuntime()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        await using var host = fixture.Host;

        await host.RestoreBackgroundStateAsync();

        Assert.AreEqual(0, fixture.Factory.CreateCalls);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.IsFalse(host.Snapshot.HasRuntimeComposition);
        Assert.IsFalse(host.Snapshot.HasBackgroundLease);
        Assert.IsFalse(fixture.Platform.IsForeground);
        Assert.AreEqual(1, fixture.Platform.RequestStopCalls);
    }

    [TestMethod]
    public async Task EnabledRestorationStartsForegroundAndAcquiresOneBackgroundLease()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;

        await host.RestoreBackgroundStateAsync();

        Assert.AreEqual(1, fixture.Factory.CreateCalls);
        Assert.AreEqual(1, fixture.Platform.EnsureServiceStartedCalls);
        Assert.AreEqual(1, fixture.Platform.EnterForegroundCalls);
        Assert.AreEqual(BackendLifetimeReason.BackgroundSync, fixture.Coordinator.ActiveReasons);
        Assert.IsTrue(host.Snapshot.HasBackgroundLease);
        Assert.IsTrue(host.Snapshot.IsForeground);
        Assert.AreEqual(
            AndroidForegroundNotificationState.BackgroundSynchronizationActive,
            fixture.Platform.LastNotificationState);
    }

    [TestMethod]
    public async Task RestorationRuntimeStartupFailureReportsDegradedAndCleansPartialComposition()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        fixture.Runtime.StartupFailure = new InvalidOperationException("runtime startup failed");
        await using var host = fixture.Host;

        await host.RestoreBackgroundStateAsync();
        var state = await host.GetBackgroundStateAsync();

        Assert.IsTrue(state.IsEnabled);
        Assert.IsTrue(state.IsDegraded);
        Assert.AreEqual(AndroidBackgroundSyncFailureKind.RuntimeLease, state.FailureKind);
        Assert.AreEqual(1, fixture.Factory.CreateCalls);
        Assert.AreEqual(1, fixture.Runtime.DisposeCalls);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.IsFalse(host.Snapshot.HasRuntimeComposition);
        Assert.IsFalse(fixture.Platform.IsForeground);
        Assert.IsFalse(fixture.Platform.IsServiceStarted);
    }

    [TestMethod]
    public async Task RepeatedRestorationDoesNotDuplicateRuntimeOrBackgroundLease()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;

        await host.RestoreBackgroundStateAsync();
        await host.RestoreBackgroundStateAsync();

        Assert.AreEqual(1, fixture.Factory.CreateCalls);
        Assert.AreEqual(1, fixture.Runtime.EnsureStartedCalls);
        Assert.AreEqual(1, fixture.Platform.EnsureServiceStartedCalls);
        Assert.AreEqual(BackendLifetimeReason.BackgroundSync, fixture.Coordinator.ActiveReasons);
    }


    [TestMethod]
    public async Task BackgroundDisableWithoutInteractiveStopsRuntimeForegroundAndService()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;
        await host.RestoreBackgroundStateAsync();

        var state = await host.SetBackgroundEnabledAsync(false);

        Assert.IsFalse(state.IsEnabled);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.AreEqual(1, fixture.Runtime.StopCalls);
        Assert.AreEqual(1, fixture.Runtime.DisposeCalls);
        Assert.IsFalse(fixture.Platform.IsForeground);
        Assert.IsFalse(fixture.Platform.IsServiceStarted);
        Assert.IsFalse(host.Snapshot.HasRuntimeComposition);
    }


    [TestMethod]
    public async Task StopFailureStillAttemptsCompositionDisposalAndServiceStop()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;
        await host.RestoreBackgroundStateAsync();
        fixture.Runtime.StopFailure = new IOException("runtime stop failed");

        var state = await host.SetBackgroundEnabledAsync(false);

        Assert.IsFalse(state.IsEnabled);
        Assert.IsTrue(state.IsDegraded);
        Assert.AreEqual(AndroidBackgroundSyncFailureKind.Shutdown, state.FailureKind);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.AreEqual(1, fixture.Runtime.DisposeCalls);
        Assert.IsFalse(host.Snapshot.HasRuntimeComposition);
        Assert.IsFalse(fixture.Platform.IsForeground);
        Assert.IsFalse(fixture.Platform.IsServiceStarted);
    }

    [TestMethod]
    public async Task ActivityReopenReusesBackgroundOwnedRuntimeWithFreshAdapter()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;
        await host.RestoreBackgroundStateAsync();
        var first = await host.AttachInteractiveClientAsync();
        await first.DisposeAsync();

        var second = await host.AttachInteractiveClientAsync();

        Assert.AreEqual(1, fixture.Factory.CreateCalls);
        Assert.AreEqual(
            BackendLifetimeReason.InteractiveUi | BackendLifetimeReason.BackgroundSync,
            fixture.Coordinator.ActiveReasons);
        Assert.AreSame(fixture.Endpoints, await second.GetEndpointsAsync());
        await second.DisposeAsync();
    }

    [TestMethod]
    public async Task InteractiveDetachPreservesBackgroundRuntime()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;
        var client = await host.AttachInteractiveClientAsync();

        Assert.AreEqual(
            BackendLifetimeReason.InteractiveUi | BackendLifetimeReason.BackgroundSync,
            fixture.Coordinator.ActiveReasons);

        await client.DisposeAsync();

        Assert.AreEqual(BackendLifetimeReason.BackgroundSync, fixture.Coordinator.ActiveReasons);
        Assert.AreEqual(0, fixture.Runtime.DisposeCalls);
        Assert.IsTrue(host.Snapshot.HasRuntimeComposition);
    }

    [TestMethod]
    public async Task InteractiveDetachWithoutBackgroundStopsAndDisposesRuntime()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        await using var host = fixture.Host;
        var client = await host.AttachInteractiveClientAsync();

        await client.DisposeAsync();

        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.AreEqual(1, fixture.Runtime.StopCalls);
        Assert.AreEqual(1, fixture.Runtime.DisposeCalls);
        Assert.IsFalse(host.Snapshot.HasRuntimeComposition);
        Assert.AreEqual(1, fixture.Platform.RequestStopCalls);
    }

    [TestMethod]
    public async Task ConcurrentInteractiveAttachmentUsesLastAttachmentWinsPolicy()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        await using var host = fixture.Host;
        var first = await host.AttachInteractiveClientAsync();

        var second = await host.AttachInteractiveClientAsync();

        Assert.Throws<ObjectDisposedException>(() => _ = first.Snapshot);
        Assert.AreEqual(1, fixture.Factory.CreateCalls);
        Assert.AreEqual(BackendLifetimeReason.InteractiveUi, fixture.Coordinator.ActiveReasons);
        Assert.AreSame(fixture.Endpoints, await second.GetEndpointsAsync());
        await first.DisposeAsync();
        await second.DisposeAsync();
    }

    [TestMethod]
    public async Task EnableAndDisableWhileInteractiveRetainsInteractiveLease()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        await using var host = fixture.Host;
        var client = await host.AttachInteractiveClientAsync();

        var enabled = await host.SetBackgroundEnabledAsync(true);
        Assert.IsTrue(enabled.IsEnabled);
        Assert.AreEqual(
            BackendLifetimeReason.InteractiveUi | BackendLifetimeReason.BackgroundSync,
            fixture.Coordinator.ActiveReasons);

        var disabled = await host.SetBackgroundEnabledAsync(false);
        Assert.IsFalse(disabled.IsEnabled);
        Assert.AreEqual(BackendLifetimeReason.InteractiveUi, fixture.Coordinator.ActiveReasons);
        Assert.IsTrue(host.Snapshot.HasRuntimeComposition);
        Assert.IsFalse(host.Snapshot.IsForeground);

        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task SecureStorageUnavailableDefersBackgroundRuntimeUntilUnlock()
    {
        using var fixture = CreateFixture(backgroundEnabled: true, secureStorageAvailable: false);
        await using var host = fixture.Host;

        await host.RestoreBackgroundStateAsync();
        var deferred = await host.GetBackgroundStateAsync();

        Assert.IsTrue(deferred.IsEnabled);
        Assert.IsTrue(deferred.IsSecureStorageDeferred);
        Assert.AreEqual(0, fixture.Factory.CreateCalls);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);

        fixture.SecureStorage.IsAvailable = true;
        await host.RestoreBackgroundStateAsync();

        Assert.AreEqual(1, fixture.Factory.CreateCalls);
        Assert.AreEqual(BackendLifetimeReason.BackgroundSync, fixture.Coordinator.ActiveReasons);
    }

    [TestMethod]
    public async Task DisabledNotificationsProduceTruthfulDegradedEnabledState()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        fixture.Platform.AreNotificationsEnabled = false;
        await using var host = fixture.Host;

        var state = await host.SetBackgroundEnabledAsync(true);

        Assert.IsTrue(state.IsEnabled);
        Assert.IsTrue(state.IsDegraded);
        Assert.AreEqual(AndroidBackgroundSyncFailureKind.ForegroundService, state.FailureKind);
        Assert.IsTrue(state.IsBackgroundLeaseActive);
    }


    [TestMethod]
    public async Task ForegroundStartFailureRollsBackEnabledSettingWithoutCreatingRuntime()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        fixture.Platform.EnterForegroundFailure = new InvalidOperationException("foreground denied");
        await using var host = fixture.Host;

        var state = await host.SetBackgroundEnabledAsync(true);

        Assert.IsFalse(state.IsEnabled);
        Assert.IsTrue(state.IsDegraded);
        Assert.AreEqual(0, fixture.Factory.CreateCalls);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.IsFalse(fixture.Platform.IsForeground);
    }


    [TestMethod]
    public async Task RuntimeStartupFailureRollsBackAndCleansPartialComposition()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        fixture.Runtime.StartupFailure = new InvalidOperationException("runtime startup failed");
        await using var host = fixture.Host;

        var state = await host.SetBackgroundEnabledAsync(true);

        Assert.IsFalse(state.IsEnabled);
        Assert.IsTrue(state.IsDegraded);
        Assert.AreEqual(AndroidBackgroundSyncFailureKind.RuntimeLease, state.FailureKind);
        Assert.AreEqual(1, fixture.Factory.CreateCalls);
        Assert.AreEqual(1, fixture.Runtime.DisposeCalls);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.IsFalse(host.Snapshot.HasRuntimeComposition);
        Assert.IsFalse(fixture.Platform.IsForeground);
    }

    [TestMethod]
    public async Task SettingPersistenceFailureReportsTruthfulPreviousState()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        fixture.Settings.WriteFailure = new IOException("settings unavailable");
        await using var host = fixture.Host;

        var state = await host.SetBackgroundEnabledAsync(true);

        Assert.IsFalse(state.IsEnabled);
        Assert.AreEqual(AndroidBackgroundSyncFailureKind.SettingPersistence, state.FailureKind);
        Assert.AreEqual(0, fixture.Factory.CreateCalls);
    }

    [TestMethod]
    public async Task EnableWriteExceptionWithAuthoritativeEnabledReadbackStillStartsBackgroundRuntime()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        fixture.Settings.WriteFailure = new IOException("write reported failure after commit");
        fixture.Settings.CommitBeforeWriteFailure = true;
        await using var host = fixture.Host;

        var state = await host.SetBackgroundEnabledAsync(true);

        Assert.IsTrue(state.IsEnabled);
        Assert.IsFalse(state.IsDegraded);
        Assert.AreEqual(AndroidBackgroundSyncFailureKind.None, state.FailureKind);
        Assert.AreEqual(BackendLifetimeReason.BackgroundSync, fixture.Coordinator.ActiveReasons);
    }

    [TestMethod]
    public async Task DisableWriteExceptionWithAuthoritativeDisabledReadbackStillStopsBackgroundRuntime()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;
        await host.RestoreBackgroundStateAsync();
        fixture.Settings.WriteFailure = new IOException("write reported failure after commit");
        fixture.Settings.CommitBeforeWriteFailure = true;

        var state = await host.SetBackgroundEnabledAsync(false);

        Assert.IsFalse(state.IsEnabled);
        Assert.IsFalse(state.IsDegraded);
        Assert.AreEqual(AndroidBackgroundSyncFailureKind.None, state.FailureKind);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.IsFalse(host.Snapshot.HasRuntimeComposition);
    }

    [TestMethod]
    public async Task SettingWriteAndReadbackFailureReportsUncertainOutcome()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        await using var host = fixture.Host;
        await host.GetBackgroundStateAsync();
        fixture.Settings.WriteFailure = new IOException("settings write failed");
        fixture.Settings.ReadFailure = new IOException("settings readback failed");

        var state = await host.SetBackgroundEnabledAsync(true);

        Assert.IsTrue(state.IsDegraded);
        Assert.AreEqual(AndroidBackgroundSyncFailureKind.Rollback, state.FailureKind);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.AreEqual(0, fixture.Factory.CreateCalls);
    }

    [TestMethod]
    public async Task DatabaseResetRestoresInteractiveAndBackgroundLeases()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;
        var client = await host.AttachInteractiveClientAsync();

        await client.ResetDatabaseAndRestartAsync();

        Assert.AreEqual(1, fixture.Runtime.ResetCalls);
        Assert.AreEqual(
            BackendLifetimeReason.InteractiveUi | BackendLifetimeReason.BackgroundSync,
            fixture.Coordinator.ActiveReasons);
        Assert.AreSame(fixture.Endpoints, await client.GetEndpointsAsync());
        await client.DisposeAsync();
    }


    [TestMethod]
    public async Task ResetFailureDoesNotConstructReplacementRuntime()
    {
        using var fixture = CreateFixture(backgroundEnabled: true);
        await using var host = fixture.Host;
        var client = await host.AttachInteractiveClientAsync();
        fixture.Runtime.ResetFailure = new IOException("reset failed");

        await Assert.ThrowsAsync<IOException>(
            () => client.ResetDatabaseAndRestartAsync());

        Assert.AreEqual(1, fixture.Factory.CreateCalls);
        Assert.AreEqual(1, fixture.Runtime.ResetCalls);
        Assert.AreEqual(BackendLifetimeReason.None, fixture.Coordinator.ActiveReasons);
        Assert.IsFalse(host.Snapshot.HasBackgroundLease);
        Assert.IsTrue(host.Snapshot.HasRuntimeComposition);
        await client.DisposeAsync();
    }

    [TestMethod]
    public async Task RepeatedDisposalDisposesRuntimeExactlyOnce()
    {
        using var fixture = CreateFixture(backgroundEnabled: false);
        var client = await fixture.Host.AttachInteractiveClientAsync();
        await client.DisposeAsync();

        await fixture.Host.DisposeAsync();
        await fixture.Host.DisposeAsync();

        Assert.AreEqual(1, fixture.Runtime.DisposeCalls);
        Assert.IsTrue(fixture.Host.Snapshot.IsDisposed);
    }

    private static AndroidRuntimeServiceHostFixture CreateFixture(
        bool backgroundEnabled,
        bool secureStorageAvailable = true)
    {
        var backendTestHost = new BackendTestHost();
        var endpoints = backendTestHost.Services.GetRequiredService<IEndpoints>();
        var runtime = new FakeBackendRuntime(endpoints);
        var coordinator = new BackendRuntimeLifetimeCoordinator(runtime);
        var composition = new BackendRuntimeComposition(runtime, coordinator, "test-data");
        var factory = new FakeAndroidRuntimeCompositionFactory(() => composition);
        var settings = new FakeBackgroundSyncSettingsStore(backgroundEnabled);
        var platform = new FakeAndroidForegroundServiceController();
        var secureStorage = new FakeAndroidSecureStorageAvailability
        {
            IsAvailable = secureStorageAvailable
        };
        var host = new AndroidRuntimeServiceHost(factory, settings, platform, secureStorage);
        return new AndroidRuntimeServiceHostFixture(
            backendTestHost,
            host,
            runtime,
            coordinator,
            factory,
            settings,
            platform,
            secureStorage,
            endpoints);
    }
}
