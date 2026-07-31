using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Contracts.Runtime;
using PasswordManagerLocal.Contracts.BackgroundSync;
using PasswordManagerLocal.Windows.Agent.Backend;
using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Agent.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;

namespace PasswordManagerLocal.Windows.Agent.Background;

public sealed class WindowsBackgroundSyncCoordinator : IWindowsBackgroundSyncCoordinator
{
    private readonly IBackgroundSyncSettingsStore _settingsStore;
    private readonly IWindowsStartupRegistration _startupRegistration;
    private readonly IWindowsAgentBackendRuntimeOwner _backendOwner;
    private readonly IWindowsAgentStateSource _agentState;
    private readonly IWindowsAgentAdmissionGate _admissionGate;
    private readonly WindowsAgentLifecycleTransitionCoordinator _lifecycleTransitions;
    private readonly object _stateGate = new();
    private IBackendRuntimeLease? _backgroundLease;
    private WindowsBackgroundSyncFailureKind _lastFailureKind;
    private IpcFailureDto? _lastFailure;

    public WindowsBackgroundSyncCoordinator(
        IBackgroundSyncSettingsStore settingsStore,
        IWindowsStartupRegistration startupRegistration,
        IWindowsAgentBackendRuntimeOwner backendOwner,
        IWindowsAgentStateSource agentState,
        IWindowsAgentAdmissionGate admissionGate,
        WindowsAgentLifecycleTransitionCoordinator lifecycleTransitions)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _startupRegistration = startupRegistration
            ?? throw new ArgumentNullException(nameof(startupRegistration));
        _backendOwner = backendOwner ?? throw new ArgumentNullException(nameof(backendOwner));
        _agentState = agentState ?? throw new ArgumentNullException(nameof(agentState));
        _admissionGate = admissionGate ?? throw new ArgumentNullException(nameof(admissionGate));
        _lifecycleTransitions = lifecycleTransitions
            ?? throw new ArgumentNullException(nameof(lifecycleTransitions));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var transition = await _lifecycleTransitions.EnterAsync(
            WindowsAgentLifecycleTransitionState.ChangingBackgroundSync,
            cancellationToken);
        ClearFailure();

        BackgroundSyncSettings settings;
        try
        {
            settings = await _settingsStore.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetFailure(
                WindowsBackgroundSyncFailureKind.SettingRead,
                "The background synchronization setting could not be read safely.",
                retryable: true,
                requiresRestart: false);
            await BestEffortStartupAndLeaseCleanupAsync();
            return;
        }

        if (!settings.IsEnabled)
        {
            var startupCleanupFailed = false;
            try
            {
                await _startupRegistration.UnregisterAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                startupCleanupFailed = true;
                SetFailure(
                    WindowsBackgroundSyncFailureKind.StartupRegistration,
                    CreateSafeMessage(
                        exception,
                        "The disabled background synchronization startup registration could not be restored."),
                    retryable: true,
                    requiresRestart: _backendOwner.Snapshot.RequiresProcessRestart);
            }

            try
            {
                await ReleaseBackgroundLeaseAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (!startupCleanupFailed)
                {
                    SetFailure(
                        WindowsBackgroundSyncFailureKind.RuntimeLease,
                        "The disabled background synchronization runtime lease could not be released.",
                        retryable: true,
                        requiresRestart: _backendOwner.Snapshot.RequiresProcessRestart);
                }
            }
            return;
        }

        try
        {
            await _startupRegistration.RegisterAsync(cancellationToken);
            var registration = await _startupRegistration.ReadAsync(cancellationToken);
            if (!registration.IsRegistered)
            {
                throw new InvalidOperationException(
                    "The automatic-startup registration could not be verified.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetFailure(
                WindowsBackgroundSyncFailureKind.StartupRegistration,
                CreateSafeMessage(
                    exception,
                    "Background synchronization startup registration could not be restored."),
                retryable: true,
                requiresRestart: false);
            return;
        }

        try
        {
            await EnsureBackgroundLeaseAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetFailure(
                WindowsBackgroundSyncFailureKind.RuntimeLease,
                CreateSafeMessage(
                    exception,
                    "Background synchronization could not start in the agent runtime."),
                retryable: true,
                requiresRestart: _backendOwner.Snapshot.RequiresProcessRestart);
        }
    }

    public Task<WindowsBackgroundSyncStateDto> GetStateAsync(
        CancellationToken cancellationToken = default) =>
        ReadAuthoritativeStateAsync(cancellationToken, includeTransition: true);

    public async Task<WindowsBackgroundSyncStateDto> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await using var transition = await _lifecycleTransitions.EnterAsync(
            WindowsAgentLifecycleTransitionState.ChangingBackgroundSync,
            cancellationToken);
        EnsureMutationAllowed();
        ClearFailure();

        var previousSettings = await _settingsStore.ReadAsync(cancellationToken);
        var previousRegistration = await _startupRegistration.ReadAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return isEnabled
            ? await EnableAsync(previousSettings, previousRegistration)
            : await DisableAsync(previousSettings, previousRegistration);
    }

    public async Task<bool> SuspendForDatabaseResetAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.ReadAsync(cancellationToken);
        await ReleaseBackgroundLeaseAsync();
        return settings.IsEnabled;
    }

    public async Task RestoreAfterDatabaseResetAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!isEnabled || _backendOwner.Snapshot.RequiresProcessRestart)
            return;

        try
        {
            await EnsureBackgroundLeaseAsync(cancellationToken);
            ClearFailure();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetFailure(
                WindowsBackgroundSyncFailureKind.RuntimeLease,
                CreateSafeMessage(
                    exception,
                    "Background synchronization could not be restored after database reset."),
                retryable: true,
                requiresRestart: _backendOwner.Snapshot.RequiresProcessRestart);
            throw;
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ReleaseBackgroundLeaseAsync();
    }

    private async Task<WindowsBackgroundSyncStateDto> EnableAsync(
        BackgroundSyncSettings previousSettings,
        WindowsStartupRegistrationSnapshot previousRegistration)
    {
        var failureKind = WindowsBackgroundSyncFailureKind.StartupRegistration;
        var acquiredLease = false;
        try
        {
            await _startupRegistration.RegisterAsync(CancellationToken.None);
            var registration = await _startupRegistration.ReadAsync(CancellationToken.None);
            if (!registration.IsRegistered)
            {
                throw new InvalidOperationException(
                    "Automatic startup registration did not match the agent command.");
            }

            failureKind = WindowsBackgroundSyncFailureKind.SettingPersistence;
            await _settingsStore.WriteAsync(
                new BackgroundSyncSettings(true),
                CancellationToken.None);

            failureKind = WindowsBackgroundSyncFailureKind.RuntimeLease;
            if (!HasBackgroundLease())
            {
                await EnsureBackgroundLeaseAsync(CancellationToken.None);
                acquiredLease = true;
            }

            var state = await ReadAuthoritativeStateAsync(
                CancellationToken.None,
                includeTransition: false);
            if (state.Consistency != WindowsBackgroundSyncConsistency.Operational)
            {
                throw new InvalidOperationException(
                    "Background synchronization did not reach an operational state.");
            }

            ClearFailure();
            return state;
        }
        catch (Exception exception)
        {
            var rollbackFailure = await RollBackEnableAsync(
                previousSettings,
                previousRegistration,
                acquiredLease);
            SetFailure(
                rollbackFailure is null
                    ? failureKind
                    : WindowsBackgroundSyncFailureKind.Rollback,
                rollbackFailure is null
                    ? CreateSafeMessage(
                        exception,
                        "Background synchronization could not be enabled. The previous setting was restored.")
                    : "Background synchronization could not be enabled and rollback did not complete safely.",
                retryable: true,
                requiresRestart: _backendOwner.Snapshot.RequiresProcessRestart);
            return await ReadAuthoritativeStateAsync(
                CancellationToken.None,
                includeTransition: false);
        }
    }

    private async Task<WindowsBackgroundSyncStateDto> DisableAsync(
        BackgroundSyncSettings previousSettings,
        WindowsStartupRegistrationSnapshot previousRegistration)
    {
        var failureKind = WindowsBackgroundSyncFailureKind.SettingPersistence;
        try
        {
            await _settingsStore.WriteAsync(
                new BackgroundSyncSettings(false),
                CancellationToken.None);

            failureKind = WindowsBackgroundSyncFailureKind.StartupRegistration;
            await _startupRegistration.UnregisterAsync(CancellationToken.None);

            failureKind = WindowsBackgroundSyncFailureKind.RuntimeLease;
            await ReleaseBackgroundLeaseAsync();

            var state = await ReadAuthoritativeStateAsync(
                CancellationToken.None,
                includeTransition: false);
            if (state.Consistency != WindowsBackgroundSyncConsistency.Disabled)
            {
                throw new InvalidOperationException(
                    "Background synchronization did not reach a disabled state.");
            }

            ClearFailure();
            return state;
        }
        catch (Exception exception)
        {
            var rollbackFailure = await RollBackDisableAsync(
                previousSettings,
                previousRegistration);
            SetFailure(
                rollbackFailure is null
                    ? failureKind
                    : WindowsBackgroundSyncFailureKind.Rollback,
                rollbackFailure is null
                    ? CreateSafeMessage(
                        exception,
                        "Background synchronization could not be disabled. The previous setting was restored.")
                    : "Background synchronization could not be disabled and rollback did not complete safely.",
                retryable: true,
                requiresRestart: _backendOwner.Snapshot.RequiresProcessRestart);
            return await ReadAuthoritativeStateAsync(
                CancellationToken.None,
                includeTransition: false);
        }
    }

    private async Task<Exception?> RollBackEnableAsync(
        BackgroundSyncSettings previousSettings,
        WindowsStartupRegistrationSnapshot previousRegistration,
        bool acquiredLease)
    {
        var failures = new List<Exception>();
        if (acquiredLease)
        {
            try { await ReleaseBackgroundLeaseAsync(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        try { await _settingsStore.WriteAsync(previousSettings, CancellationToken.None); }
        catch (Exception exception) { failures.Add(exception); }
        try { await _startupRegistration.RestoreAsync(previousRegistration, CancellationToken.None); }
        catch (Exception exception) { failures.Add(exception); }
        return Combine(failures);
    }

    private async Task<Exception?> RollBackDisableAsync(
        BackgroundSyncSettings previousSettings,
        WindowsStartupRegistrationSnapshot previousRegistration)
    {
        var failures = new List<Exception>();
        try { await _settingsStore.WriteAsync(previousSettings, CancellationToken.None); }
        catch (Exception exception) { failures.Add(exception); }
        try { await _startupRegistration.RestoreAsync(previousRegistration, CancellationToken.None); }
        catch (Exception exception) { failures.Add(exception); }
        if (previousSettings.IsEnabled && !HasBackgroundLease() &&
            !_backendOwner.Snapshot.RequiresProcessRestart)
        {
            try { await EnsureBackgroundLeaseAsync(CancellationToken.None); }
            catch (Exception exception) { failures.Add(exception); }
        }
        return Combine(failures);
    }

    private async Task EnsureBackgroundLeaseAsync(CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (_backgroundLease is not null)
                return;
        }

        var lease = await _backendOwner.AcquireBackgroundSyncLeaseAsync(cancellationToken);
        lock (_stateGate)
        {
            if (_backgroundLease is null)
            {
                _backgroundLease = lease;
                return;
            }
        }

        await lease.DisposeAsync();
    }

    private async Task ReleaseBackgroundLeaseAsync()
    {
        IBackendRuntimeLease? lease;
        lock (_stateGate)
        {
            lease = _backgroundLease;
            _backgroundLease = null;
        }

        if (lease is not null)
            await lease.DisposeAsync();
    }

    private async Task<WindowsBackgroundSyncStateDto> ReadAuthoritativeStateAsync(
        CancellationToken cancellationToken,
        bool includeTransition)
    {
        BackgroundSyncSettings settings;
        try
        {
            settings = await _settingsStore.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = GetFailure() ?? CreateFailure(
                "The background synchronization setting is unavailable.",
                retryable: true,
                requiresRestart: _backendOwner.Snapshot.RequiresProcessRestart);
            return CreateState(
                isEnabled: false,
                isStartupRegistered: false,
                WindowsBackgroundSyncConsistency.Unavailable,
                GetFailureKindOr(WindowsBackgroundSyncFailureKind.SettingRead),
                failure);
        }

        WindowsStartupRegistrationSnapshot registration;
        try
        {
            registration = await _startupRegistration.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = GetFailure() ?? CreateFailure(
                "Windows automatic startup registration is unavailable.",
                retryable: true,
                requiresRestart: false);
            return CreateState(
                settings.IsEnabled,
                isStartupRegistered: false,
                WindowsBackgroundSyncConsistency.Unavailable,
                GetFailureKindOr(WindowsBackgroundSyncFailureKind.StartupRegistration),
                failure);
        }

        var transitionInProgress = includeTransition &&
            _lifecycleTransitions.State != WindowsAgentLifecycleTransitionState.Idle;
        var leaseActive = HasBackgroundLease();
        var runtimeState = _backendOwner.Snapshot.Runtime.State;
        var runtimeRunning = IsRuntimeRunning(runtimeState);
        var failureKind = GetFailureKind();
        var failureSnapshot = GetFailure();

        if (transitionInProgress)
        {
            return new WindowsBackgroundSyncStateDto(
                settings.IsEnabled,
                registration.IsRegistered,
                leaseActive,
                runtimeRunning,
                IsTransitionInProgress: true,
                WindowsBackgroundSyncConsistency.Transitioning,
                WindowsBackgroundSyncFailureKind.None,
                Failure: null);
        }

        if (failureSnapshot is not null)
        {
            var consistency = IsInconsistent(settings, registration, leaseActive)
                ? WindowsBackgroundSyncConsistency.Inconsistent
                : WindowsBackgroundSyncConsistency.Degraded;
            return new WindowsBackgroundSyncStateDto(
                settings.IsEnabled,
                registration.IsRegistered,
                leaseActive,
                runtimeRunning,
                IsTransitionInProgress: false,
                consistency,
                failureKind,
                failureSnapshot);
        }

        if (!settings.IsEnabled && !registration.EntryExists && !leaseActive)
        {
            return new WindowsBackgroundSyncStateDto(
                IsEnabled: false,
                IsStartupRegistered: false,
                IsBackgroundLeaseActive: false,
                IsRuntimeRunning: runtimeRunning,
                IsTransitionInProgress: false,
                WindowsBackgroundSyncConsistency.Disabled,
                WindowsBackgroundSyncFailureKind.None,
                Failure: null);
        }

        if (settings.IsEnabled && registration.IsRegistered && leaseActive &&
            runtimeState == BackendRuntimeState.Ready)
        {
            return new WindowsBackgroundSyncStateDto(
                IsEnabled: true,
                IsStartupRegistered: true,
                IsBackgroundLeaseActive: true,
                IsRuntimeRunning: true,
                IsTransitionInProgress: false,
                WindowsBackgroundSyncConsistency.Operational,
                WindowsBackgroundSyncFailureKind.None,
                Failure: null);
        }

        var inconsistent = IsInconsistent(settings, registration, leaseActive);
        var consistencyFailureKind = GetConsistencyFailureKind(
            settings,
            registration);
        return new WindowsBackgroundSyncStateDto(
            settings.IsEnabled,
            registration.IsRegistered,
            leaseActive,
            runtimeRunning,
            IsTransitionInProgress: false,
            inconsistent
                ? WindowsBackgroundSyncConsistency.Inconsistent
                : WindowsBackgroundSyncConsistency.Degraded,
            consistencyFailureKind,
            CreateFailure(
                consistencyFailureKind == WindowsBackgroundSyncFailureKind.StartupRegistration
                    ? "Windows automatic startup does not match the authoritative background synchronization setting."
                    : inconsistent
                        ? "The persisted setting and runtime lease are inconsistent."
                        : "Background synchronization is enabled but is not currently operational.",
                retryable: true,
                requiresRestart: _backendOwner.Snapshot.RequiresProcessRestart));
    }

    private WindowsBackgroundSyncStateDto CreateState(
        bool isEnabled,
        bool isStartupRegistered,
        WindowsBackgroundSyncConsistency consistency,
        WindowsBackgroundSyncFailureKind failureKind,
        IpcFailureDto failure) => new(
            isEnabled,
            isStartupRegistered,
            HasBackgroundLease(),
            IsRuntimeRunning(_backendOwner.Snapshot.Runtime.State),
            IsTransitionInProgress: false,
            consistency,
            failureKind,
            failure);

    private void EnsureMutationAllowed()
    {
        var backend = _backendOwner.Snapshot;
        if (_agentState.State != AgentState.Running ||
            !_admissionGate.IsOpen ||
            backend.RequiresProcessRestart ||
            backend.IsResetting ||
            backend.State is WindowsAgentBackendOwnerState.RestartRequired or
                WindowsAgentBackendOwnerState.Stopping or
                WindowsAgentBackendOwnerState.Stopped or
                WindowsAgentBackendOwnerState.Failed)
        {
            throw new InvalidOperationException(
                "The Windows agent is not available for background synchronization changes.");
        }
    }

    private async Task BestEffortStartupAndLeaseCleanupAsync()
    {
        try { await _startupRegistration.UnregisterAsync(CancellationToken.None); }
        catch { }
        try { await ReleaseBackgroundLeaseAsync(); }
        catch { }
    }

    private bool HasBackgroundLease()
    {
        lock (_stateGate)
            return _backgroundLease is not null;
    }

    private void ClearFailure()
    {
        lock (_stateGate)
        {
            _lastFailureKind = WindowsBackgroundSyncFailureKind.None;
            _lastFailure = null;
        }
    }

    private WindowsBackgroundSyncFailureKind GetFailureKind()
    {
        lock (_stateGate)
            return _lastFailureKind;
    }

    private WindowsBackgroundSyncFailureKind GetFailureKindOr(
        WindowsBackgroundSyncFailureKind fallback)
    {
        var kind = GetFailureKind();
        return kind == WindowsBackgroundSyncFailureKind.None ? fallback : kind;
    }

    private IpcFailureDto? GetFailure()
    {
        lock (_stateGate)
            return _lastFailure;
    }

    private void SetFailure(
        WindowsBackgroundSyncFailureKind failureKind,
        string safeMessage,
        bool retryable,
        bool requiresRestart)
    {
        if (failureKind == WindowsBackgroundSyncFailureKind.None)
            throw new ArgumentOutOfRangeException(nameof(failureKind));

        lock (_stateGate)
        {
            _lastFailureKind = failureKind;
            _lastFailure = CreateFailure(safeMessage, retryable, requiresRestart);
        }
    }

    private static IpcFailureDto CreateFailure(
        string safeMessage,
        bool retryable,
        bool requiresRestart) => new(
            IpcFailureKind.BackgroundConfiguration,
            safeMessage,
            DateTimeOffset.UtcNow,
            retryable,
            requiresRestart);

    private static bool IsInconsistent(
        BackgroundSyncSettings settings,
        WindowsStartupRegistrationSnapshot registration,
        bool leaseActive) =>
        registration.EntryExists != registration.IsRegistered ||
        (!settings.IsEnabled && (registration.EntryExists || leaseActive)) ||
        (settings.IsEnabled && !registration.IsRegistered);

    private static WindowsBackgroundSyncFailureKind GetConsistencyFailureKind(
        BackgroundSyncSettings settings,
        WindowsStartupRegistrationSnapshot registration)
    {
        if (registration.EntryExists != registration.IsRegistered ||
            settings.IsEnabled != registration.IsRegistered)
        {
            return WindowsBackgroundSyncFailureKind.StartupRegistration;
        }

        return WindowsBackgroundSyncFailureKind.RuntimeLease;
    }

    private static bool IsRuntimeRunning(BackendRuntimeState state) => state is
        BackendRuntimeState.Starting or
        BackendRuntimeState.Ready or
        BackendRuntimeState.WaitingForDeviceUnlock;

    private static string CreateSafeMessage(Exception exception, string fallback) =>
        exception is UnauthorizedAccessException
            ? "Windows automatic startup could not be changed for the current user."
            : exception is InvalidDataException
                ? "The background synchronization setting is invalid."
                : fallback;

    private static Exception? Combine(IReadOnlyCollection<Exception> failures) =>
        failures.Count switch
        {
            0 => null,
            1 => failures.First(),
            _ => new AggregateException(failures)
        };
}
