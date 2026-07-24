using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Android.Runtime;

public sealed class AndroidRuntimeServiceHost : IAsyncDisposable
{
    private readonly IAndroidRuntimeCompositionFactory _compositionFactory;
    private readonly IBackgroundSyncSettingsStore _settingsStore;
    private readonly IAndroidForegroundServiceController _foregroundServiceController;
    private readonly IAndroidSecureStorageAvailability _secureStorageAvailability;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private BackendRuntimeComposition? _composition;
    private IBackendRuntimeLease? _backgroundLease;
    private AndroidServiceFrontendBackendClient? _interactiveClient;
    private AndroidBackgroundSyncServiceState _backgroundState;
    private bool _settingsLoaded;
    private bool _disposed;

    public AndroidRuntimeServiceHost(
        IAndroidRuntimeCompositionFactory compositionFactory,
        IBackgroundSyncSettingsStore settingsStore,
        IAndroidForegroundServiceController foregroundServiceController,
        IAndroidSecureStorageAvailability secureStorageAvailability)
    {
        _compositionFactory = compositionFactory
            ?? throw new ArgumentNullException(nameof(compositionFactory));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _foregroundServiceController = foregroundServiceController
            ?? throw new ArgumentNullException(nameof(foregroundServiceController));
        _secureStorageAvailability = secureStorageAvailability
            ?? throw new ArgumentNullException(nameof(secureStorageAvailability));
        _backgroundState = CreateInitialState();
    }

    public AndroidRuntimeServiceSnapshot Snapshot
    {
        get
        {
            var composition = _composition;
            return new AndroidRuntimeServiceSnapshot(
                composition is not null,
                _interactiveClient is not null,
                _backgroundLease is not null,
                _backgroundState.IsForegroundActive,
                _disposed,
                composition?.LifetimeCoordinator.ActiveReasons ?? BackendLifetimeReason.None);
        }
    }

    public async Task RestoreBackgroundStateAsync(CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await LoadSettingsLockedAsync(cancellationToken);
            if (!_backgroundState.IsEnabled)
            {
                await DisableBackgroundRuntimeLockedAsync(requestServiceStop: true);
                return;
            }

            try
            {
                await EnsureBackgroundRuntimeLockedAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                var cleanupFailed = false;
                try
                {
                    await DisableBackgroundRuntimeLockedAsync(requestServiceStop: true);
                }
                catch
                {
                    cleanupFailed = true;
                }

                _backgroundState = CreateFailureState(
                    true,
                    cleanupFailed
                        ? AndroidBackgroundSyncFailureKind.Shutdown
                        : AndroidBackgroundSyncFailureKind.RuntimeLease,
                    cleanupFailed
                        ? "Background synchronization restoration failed and cleanup did not complete cleanly."
                        : "Background synchronization could not be restored.");
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task<AndroidServiceFrontendBackendClient> AttachInteractiveClientAsync(
        CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await LoadSettingsLockedAsync(cancellationToken);
            var previousClient = _interactiveClient;
            if (previousClient is not null)
            {
                _interactiveClient = null;
                try
                {
                    await previousClient.CloseFromHostAsync();
                }
                catch
                {
                    await DisposeCompositionWhenUnownedLockedAsync();
                    throw;
                }
                finally
                {
                    previousClient.CompleteDisposalFromHost();
                }
            }

            if (_backgroundState.IsEnabled && _backgroundLease is null)
            {
                try
                {
                    await EnsureBackgroundRuntimeLockedAsync(cancellationToken);
                }
                catch when (!cancellationToken.IsCancellationRequested)
                {
                    _foregroundServiceController.ExitForeground();
                    _backgroundState = CreateFailureState(
                        true,
                        AndroidBackgroundSyncFailureKind.RuntimeLease,
                        "Background synchronization could not be restored.");
                }
            }

            var composition = EnsureCompositionLocked();
            var client = new AndroidServiceFrontendBackendClient(
                this,
                composition.Runtime,
                composition.LifetimeCoordinator);
            _interactiveClient = client;

            try
            {
                await client.OpenFromHostAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _interactiveClient = null;
                client.CompleteDisposalFromHost();
                await DisposeCompositionWhenUnownedLockedAsync();
                throw;
            }
            catch
            {
            }

            return client;
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task EnsureInteractiveConnectionAsync(
        AndroidServiceFrontendBackendClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            EnsureCurrentInteractiveClient(client);
            await client.OpenFromHostAsync(cancellationToken);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task DetachInteractiveClientAsync(AndroidServiceFrontendBackendClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        await _transitionLock.WaitAsync(CancellationToken.None);
        Exception? failure = null;
        try
        {
            if (!ReferenceEquals(_interactiveClient, client))
            {
                client.CompleteDisposalFromHost();
                return;
            }

            _interactiveClient = null;
            try
            {
                await client.CloseFromHostAsync();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                client.CompleteDisposalFromHost();
            }

            try
            {
                await DisposeCompositionWhenUnownedLockedAsync();
                if (_composition is null && _backgroundLease is null)
                    _foregroundServiceController.RequestStop();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }
        }
        finally
        {
            _transitionLock.Release();
        }

        if (failure is not null)
            throw failure;
    }

    public async Task<AndroidBackgroundSyncServiceState> GetBackgroundStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await LoadSettingsLockedAsync(cancellationToken);
            return _backgroundState;
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task<AndroidBackgroundSyncServiceState> SetBackgroundEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await LoadSettingsLockedAsync(cancellationToken);
            _backgroundState = _backgroundState with { IsTransitionInProgress = true };

            return isEnabled
                ? await EnableBackgroundLockedAsync(cancellationToken)
                : await DisableBackgroundLockedAsync(cancellationToken);
        }
        finally
        {
            _backgroundState = _backgroundState with { IsTransitionInProgress = false };
            _transitionLock.Release();
        }
    }

    public async Task ResetDatabaseAsync(
        AndroidServiceFrontendBackendClient client,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        await _transitionLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            EnsureCurrentInteractiveClient(client);
            var composition = _composition
                ?? throw new InvalidOperationException("The Android runtime composition is unavailable.");
            var restoreBackground = _backgroundState.IsEnabled;

            await client.CloseFromHostAsync();
            await ReleaseBackgroundLeaseLockedAsync();

            IBackendRuntimeLease? interactiveLease = null;
            IInteractiveBackendSession? interactiveSession = null;
            try
            {
                interactiveLease = await composition.LifetimeCoordinator.ResetDatabaseAndAcquireAsync(
                    BackendLifetimeReason.InteractiveUi,
                    cancellationToken);
                interactiveSession = await composition.Runtime.OpenInteractiveSessionAsync(cancellationToken);
                await client.AdoptResetConnectionFromHostAsync(interactiveLease, interactiveSession);
                interactiveLease = null;
                interactiveSession = null;

                if (restoreBackground)
                    await AcquireBackgroundLeaseLockedAsync(cancellationToken);
            }
            catch
            {
                if (interactiveSession is not null)
                    await interactiveSession.DisposeAsync();
                if (interactiveLease is not null)
                    await interactiveLease.DisposeAsync();

                if (restoreBackground && _backgroundLease is null)
                {
                    _foregroundServiceController.ExitForeground();
                    _backgroundState = CreateFailureState(
                        true,
                        AndroidBackgroundSyncFailureKind.RuntimeLease,
                        "Background synchronization could not be restored after database reset.");
                }

                throw;
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transitionLock.WaitAsync(CancellationToken.None);
        Exception? failure = null;
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            var client = _interactiveClient;
            _interactiveClient = null;
            if (client is not null)
            {
                try
                {
                    await client.CloseFromHostAsync();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    client.CompleteDisposalFromHost();
                }
            }

            try
            {
                await ReleaseBackgroundLeaseLockedAsync();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }

            try
            {
                await DisposeCompositionLockedAsync();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }

            try
            {
                _foregroundServiceController.ExitForeground();
                _foregroundServiceController.RequestStop();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }

            _backgroundState = _backgroundState with
            {
                IsBackgroundLeaseActive = false,
                IsForegroundActive = false,
                IsDegraded = failure is not null,
                FailureKind = failure is null
                    ? AndroidBackgroundSyncFailureKind.None
                    : AndroidBackgroundSyncFailureKind.Shutdown,
                SafeMessage = failure is null ? null : "Android runtime shutdown did not complete cleanly."
            };
        }
        finally
        {
            _transitionLock.Release();
            GC.SuppressFinalize(this);
        }

        if (failure is not null)
            throw failure;
    }

    private async Task<AndroidBackgroundSyncServiceState> EnableBackgroundLockedAsync(
        CancellationToken cancellationToken)
    {
        if (_backgroundState.IsEnabled && _backgroundLease is not null)
        {
            _backgroundState = CreateOperationalState(true);
            return _backgroundState;
        }

        try
        {
            await _settingsStore.WriteAsync(new BackgroundSyncSettings(true), cancellationToken);
            _backgroundState = _backgroundState with { IsEnabled = true };
        }
        catch
        {
            var authoritativeEnabled = await TryReadAuthoritativeEnabledLockedAsync();
            if (authoritativeEnabled == true)
            {
                _backgroundState = _backgroundState with { IsEnabled = true };
            }
            else
            {
                _backgroundState = CreateFailureState(
                    authoritativeEnabled ?? _backgroundState.IsEnabled,
                    authoritativeEnabled.HasValue
                        ? AndroidBackgroundSyncFailureKind.SettingPersistence
                        : AndroidBackgroundSyncFailureKind.Rollback,
                    authoritativeEnabled.HasValue
                        ? "Background synchronization could not be enabled."
                        : "Background synchronization state is uncertain.");
                return _backgroundState;
            }
        }

        try
        {
            await EnsureBackgroundRuntimeLockedAsync(cancellationToken);
            return _backgroundState;
        }
        catch
        {
            var rollbackUncertain = false;
            try
            {
                await _settingsStore.WriteAsync(new BackgroundSyncSettings(false), CancellationToken.None);
                _backgroundState = _backgroundState with { IsEnabled = false };
            }
            catch
            {
                var authoritativeEnabled = await TryReadAuthoritativeEnabledLockedAsync();
                if (authoritativeEnabled.HasValue)
                    _backgroundState = _backgroundState with { IsEnabled = authoritativeEnabled.Value };
                else
                    rollbackUncertain = true;
            }

            var cleanupFailed = false;
            try
            {
                await DisableBackgroundRuntimeLockedAsync(requestServiceStop: _interactiveClient is null);
            }
            catch
            {
                cleanupFailed = true;
            }

            _backgroundState = CreateFailureState(
                _backgroundState.IsEnabled,
                rollbackUncertain || cleanupFailed
                    ? AndroidBackgroundSyncFailureKind.Rollback
                    : AndroidBackgroundSyncFailureKind.RuntimeLease,
                rollbackUncertain || cleanupFailed
                    ? "Background synchronization state is uncertain."
                    : "Background synchronization could not be started.");
            return _backgroundState;
        }
    }

    private async Task<AndroidBackgroundSyncServiceState> DisableBackgroundLockedAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _settingsStore.WriteAsync(new BackgroundSyncSettings(false), cancellationToken);
            _backgroundState = _backgroundState with { IsEnabled = false };
        }
        catch
        {
            var authoritativeEnabled = await TryReadAuthoritativeEnabledLockedAsync();
            if (authoritativeEnabled == false)
            {
                _backgroundState = _backgroundState with { IsEnabled = false };
            }
            else
            {
                _backgroundState = CreateFailureState(
                    authoritativeEnabled ?? _backgroundState.IsEnabled,
                    authoritativeEnabled.HasValue
                        ? AndroidBackgroundSyncFailureKind.SettingPersistence
                        : AndroidBackgroundSyncFailureKind.Rollback,
                    authoritativeEnabled.HasValue
                        ? "Background synchronization could not be disabled."
                        : "Background synchronization state is uncertain.");
                return _backgroundState;
            }
        }

        try
        {
            await DisableBackgroundRuntimeLockedAsync(requestServiceStop: true);
            _backgroundState = CreateOperationalState(false);
        }
        catch
        {
            _backgroundState = CreateFailureState(
                false,
                AndroidBackgroundSyncFailureKind.Shutdown,
                "Background synchronization is disabled, but cleanup did not complete cleanly.");
        }

        return _backgroundState;
    }

    private async Task EnsureBackgroundRuntimeLockedAsync(CancellationToken cancellationToken)
    {
        if (!_secureStorageAvailability.IsAvailable)
        {
            _backgroundState = new AndroidBackgroundSyncServiceState(
                IsEnabled: true,
                IsAvailable: true,
                IsDegraded: true,
                IsTransitionInProgress: false,
                AndroidBackgroundSyncFailureKind.SecureStorageDeferred,
                "Background synchronization is waiting for device unlock.",
                IsBackgroundLeaseActive: false,
                IsForegroundActive: false,
                IsSecureStorageDeferred: true);
            _foregroundServiceController.ExitForeground();
            _foregroundServiceController.RequestStop();
            return;
        }

        _foregroundServiceController.EnsureServiceStarted();
        _foregroundServiceController.EnterForeground(
            AndroidForegroundNotificationState.BackgroundSynchronizationActive);
        var composition = EnsureCompositionLocked();
        if (_backgroundLease is null)
        {
            _backgroundLease = await composition.LifetimeCoordinator.AcquireAsync(
                BackendLifetimeReason.BackgroundSync,
                cancellationToken);
        }

        _backgroundState = CreateOperationalState(true) with
        {
            IsDegraded = !_foregroundServiceController.AreNotificationsEnabled,
            FailureKind = _foregroundServiceController.AreNotificationsEnabled
                ? AndroidBackgroundSyncFailureKind.None
                : AndroidBackgroundSyncFailureKind.ForegroundService,
            SafeMessage = _foregroundServiceController.AreNotificationsEnabled
                ? null
                : "Background synchronization is active, but notifications are disabled."
        };
    }

    private async Task DisableBackgroundRuntimeLockedAsync(bool requestServiceStop)
    {
        Exception? failure = null;
        try
        {
            await ReleaseBackgroundLeaseLockedAsync();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            _foregroundServiceController.ExitForeground();
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(failure, exception);
        }

        _backgroundState = _backgroundState with
        {
            IsBackgroundLeaseActive = false,
            IsForegroundActive = false,
            IsSecureStorageDeferred = false
        };

        try
        {
            await DisposeCompositionWhenUnownedLockedAsync();
        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(failure, exception);
        }

        if (requestServiceStop)
        {
            try
            {
                _foregroundServiceController.RequestStop();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }
        }

        if (failure is not null)
            throw failure;
    }

    private async Task AcquireBackgroundLeaseLockedAsync(CancellationToken cancellationToken)
    {
        if (_backgroundLease is not null)
            return;

        var composition = EnsureCompositionLocked();
        _backgroundLease = await composition.LifetimeCoordinator.AcquireAsync(
            BackendLifetimeReason.BackgroundSync,
            cancellationToken);
        _backgroundState = _backgroundState with { IsBackgroundLeaseActive = true };
    }

    private async Task ReleaseBackgroundLeaseLockedAsync()
    {
        var lease = _backgroundLease;
        _backgroundLease = null;
        _backgroundState = _backgroundState with { IsBackgroundLeaseActive = false };
        if (lease is not null)
            await lease.DisposeAsync();
    }

    private BackendRuntimeComposition EnsureCompositionLocked()
    {
        if (_composition is not null)
            return _composition;

        _composition = _compositionFactory.Create()
            ?? throw new InvalidOperationException("The Android runtime composition factory returned no composition.");
        return _composition;
    }

    private async Task DisposeCompositionWhenUnownedLockedAsync()
    {
        if (_interactiveClient is not null || _backgroundLease is not null)
            return;

        await DisposeCompositionLockedAsync();
    }

    private async Task DisposeCompositionLockedAsync()
    {
        var composition = _composition;
        _composition = null;
        if (composition is not null)
            await composition.Runtime.DisposeAsync();
    }

    private async Task<bool?> TryReadAuthoritativeEnabledLockedAsync()
    {
        try
        {
            var settings = await _settingsStore.ReadAsync(CancellationToken.None);
            return settings.IsEnabled;
        }
        catch
        {
            return null;
        }
    }

    private async Task LoadSettingsLockedAsync(CancellationToken cancellationToken)
    {
        if (_settingsLoaded)
            return;

        try
        {
            var settings = await _settingsStore.ReadAsync(cancellationToken);
            _settingsLoaded = true;
            _backgroundState = CreateOperationalState(settings.IsEnabled);
        }
        catch
        {
            _settingsLoaded = true;
            _backgroundState = CreateFailureState(
                false,
                AndroidBackgroundSyncFailureKind.SettingRead,
                "Background synchronization settings are unavailable.");
        }
    }

    private void EnsureCurrentInteractiveClient(AndroidServiceFrontendBackendClient client)
    {
        if (!ReferenceEquals(_interactiveClient, client))
            throw new InvalidOperationException("The Android interactive service attachment is no longer active.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AndroidRuntimeServiceHost));
    }

    private AndroidBackgroundSyncServiceState CreateOperationalState(bool isEnabled) => new(
        isEnabled,
        IsAvailable: true,
        IsDegraded: false,
        IsTransitionInProgress: false,
        AndroidBackgroundSyncFailureKind.None,
        SafeMessage: null,
        IsBackgroundLeaseActive: isEnabled && _backgroundLease is not null,
        IsForegroundActive: isEnabled && _backgroundLease is not null,
        IsSecureStorageDeferred: false);

    private AndroidBackgroundSyncServiceState CreateFailureState(
        bool isEnabled,
        AndroidBackgroundSyncFailureKind failureKind,
        string safeMessage) => new(
            isEnabled,
            IsAvailable: true,
            IsDegraded: true,
            IsTransitionInProgress: false,
            failureKind,
            safeMessage,
            IsBackgroundLeaseActive: false,
            IsForegroundActive: false,
            IsSecureStorageDeferred: false);

    private AndroidBackgroundSyncServiceState CreateInitialState() => new(
        IsEnabled: false,
        IsAvailable: true,
        IsDegraded: false,
        IsTransitionInProgress: false,
        AndroidBackgroundSyncFailureKind.None,
        SafeMessage: null,
        IsBackgroundLeaseActive: false,
        IsForegroundActive: false,
        IsSecureStorageDeferred: false);
}
