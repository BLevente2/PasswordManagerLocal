using PasswordManagerLocal.Windows.Agent.Backend;
using PasswordManagerLocal.Windows.Agent.Endpoint;
using PasswordManagerLocal.Windows.Agent.Tray;
using PasswordManagerLocal.Windows.Agent.Ui;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Coordination;
using PasswordManagerLocal.Windows.Ipc.Server;
using System.Runtime.ExceptionServices;

namespace PasswordManagerLocal.Windows.Agent.Hosting;

public sealed class WindowsAgentHost : IWindowsAgentHost
{
    private readonly IProcessInstanceLock _processLock;
    private readonly IWindowsIpcServerHost _controlServer;
    private readonly IWindowsAgentEndpointHost _endpointHost;
    private readonly IWindowsAgentBackendRuntimeOwner _backendOwner;
    private readonly ITrayIconController _trayIcon;
    private readonly IWindowsUiOpenService _uiOpenService;
    private readonly IWindowsUiCloseService _uiCloseService;
    private readonly WindowsAgentShutdownCoordinator _shutdownCoordinator;
    private readonly WindowsAgentStateStore _stateStore;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _shutdownGate = new();
    private Task? _shutdownTask;
    private int _started;
    private int _backendStartupAttempted;
    private int _controlServerStartupAttempted;
    private int _endpointHostStartupAttempted;
    private int _trayStartupAttempted;
    private int _shutdownStarted;
    private int _disposeStarted;

    public WindowsAgentHost(
        IProcessInstanceLock processLock,
        IWindowsIpcServerHost controlServer,
        IWindowsAgentEndpointHost endpointHost,
        IWindowsAgentBackendRuntimeOwner backendOwner,
        ITrayIconController trayIcon,
        IWindowsUiOpenService uiOpenService,
        IWindowsUiCloseService uiCloseService,
        WindowsAgentShutdownCoordinator shutdownCoordinator,
        WindowsAgentStateStore stateStore)
    {
        _processLock = processLock ?? throw new ArgumentNullException(nameof(processLock));
        _controlServer = controlServer ?? throw new ArgumentNullException(nameof(controlServer));
        _endpointHost = endpointHost ?? throw new ArgumentNullException(nameof(endpointHost));
        _backendOwner = backendOwner ?? throw new ArgumentNullException(nameof(backendOwner));
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        _uiOpenService = uiOpenService ?? throw new ArgumentNullException(nameof(uiOpenService));
        _uiCloseService = uiCloseService ?? throw new ArgumentNullException(nameof(uiCloseService));
        _shutdownCoordinator = shutdownCoordinator ?? throw new ArgumentNullException(nameof(shutdownCoordinator));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _shutdownCoordinator.ShutdownRequested += HandleShutdownRequested;
        _trayIcon.OpenRequested += HandleOpenRequested;
        _trayIcon.ExitRequested += HandleExitRequested;
        _backendOwner.StateChanged += HandleBackendOwnerStateChanged;
        _endpointHost.StateChanged += HandleEndpointHostStateChanged;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The Windows agent host has already started.");

        Exception? startupFailure = null;
        var ownershipUnavailable = false;
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            _processLock.EnsureOwnership();
            Interlocked.Exchange(ref _backendStartupAttempted, 1);
            await _backendOwner.StartAsync(cancellationToken);
            Interlocked.Exchange(ref _controlServerStartupAttempted, 1);
            await _controlServer.StartAsync(cancellationToken);
            EnsureBackendOwnerReadyForEndpointStart();
            Interlocked.Exchange(ref _endpointHostStartupAttempted, 1);
            await _endpointHost.StartAsync(cancellationToken);
            Interlocked.Exchange(ref _trayStartupAttempted, 1);
            await _trayIcon.InitializeAsync(cancellationToken);
            if (Volatile.Read(ref _shutdownStarted) == 0)
            {
                _stateStore.MarkRunning(DateTimeOffset.UtcNow);
                _ = ObserveControlServerAsync();
                _ = ObserveEndpointHostAsync();
            }
        }
        catch (ProcessInstanceAlreadyOwnedException exception)
        {
            _processLock.Dispose();
            Interlocked.Exchange(ref _shutdownStarted, 1);
            startupFailure = exception;
            ownershipUnavailable = true;
        }
        catch (Exception exception)
        {
            _stateStore.MarkFailed("The Windows agent shell failed to start.");
            startupFailure = exception;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (startupFailure is null)
            return;

        if (!ownershipUnavailable)
        {
            try
            {
                await ShutdownAsync(CancellationToken.None);
            }
            catch (Exception shutdownFailure)
            {
                startupFailure = new AggregateException(startupFailure, shutdownFailure);
            }
        }

        ExceptionDispatchInfo.Capture(startupFailure).Throw();
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        Task shutdownTask;
        lock (_shutdownGate)
            shutdownTask = _shutdownTask ??= ShutdownCoreAsync();

        return cancellationToken.CanBeCanceled
            ? shutdownTask.WaitAsync(cancellationToken)
            : shutdownTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        Exception? failure = null;
        try
        {
            await ShutdownAsync();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            _shutdownCoordinator.ShutdownRequested -= HandleShutdownRequested;
            _trayIcon.OpenRequested -= HandleOpenRequested;
            _trayIcon.ExitRequested -= HandleExitRequested;
            _backendOwner.StateChanged -= HandleBackendOwnerStateChanged;
            _endpointHost.StateChanged -= HandleEndpointHostStateChanged;
            _lifecycleGate.Dispose();
            GC.SuppressFinalize(this);
        }

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task ShutdownCoreAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;

        await _lifecycleGate.WaitAsync();
        try
        {
            _stateStore.MarkStopping();
            var failures = new List<Exception>();

            async Task CaptureAsync(Func<Task> cleanup)
            {
                try
                {
                    await cleanup();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            void Capture(Action cleanup)
            {
                try
                {
                    cleanup();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            // Endpoint admission closes and admitted work drains before runtime disposal.
            if (Volatile.Read(ref _endpointHostStartupAttempted) != 0)
                await CaptureAsync(() => _endpointHost.StopAsync());

            await CaptureAsync(async () =>
            {
                await _uiCloseService.RequestCloseAsync();
            });

            if (Volatile.Read(ref _backendStartupAttempted) != 0)
                await CaptureAsync(() => _backendOwner.StopAsync());

            if (Volatile.Read(ref _controlServerStartupAttempted) != 0)
                await CaptureAsync(() => _controlServer.StopAsync());

            if (Volatile.Read(ref _backendStartupAttempted) != 0)
                await CaptureAsync(async () => await _backendOwner.DisposeAsync());

            if (Volatile.Read(ref _endpointHostStartupAttempted) != 0)
                await CaptureAsync(async () => await _endpointHost.DisposeAsync());

            if (Volatile.Read(ref _controlServerStartupAttempted) != 0)
                await CaptureAsync(async () => await _controlServer.DisposeAsync());

            if (Volatile.Read(ref _trayStartupAttempted) != 0)
                await CaptureAsync(async () => await _trayIcon.DisposeAsync());

            // The process lock is deliberately released after every other owned resource.
            Capture(_processLock.Dispose);

            if (failures.Count == 0)
            {
                _stateStore.MarkStopped();
                return;
            }

            _stateStore.MarkShutdownFailed(
                "The Windows agent could not shut down all owned resources safely.");
            throw failures.Count == 1
                ? failures[0]
                : new AggregateException(failures);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void EnsureBackendOwnerReadyForEndpointStart()
    {
        var snapshot = _backendOwner.Snapshot;
        if (snapshot.State != WindowsAgentBackendOwnerState.Ready ||
            snapshot.IsResetting ||
            snapshot.RequiresProcessRestart)
        {
            throw new InvalidOperationException(
                "The Windows agent endpoint listener cannot start before the backend owner is ready.");
        }
    }

    private async Task ObserveControlServerAsync()
    {
        try { await _controlServer.Completion; } catch { }
        if (Volatile.Read(ref _shutdownStarted) == 0 &&
            _controlServer.ListenerFailure is not null)
        {
            _stateStore.MarkFailed("The Windows agent control listener failed.");
            _shutdownCoordinator.RequestShutdown();
        }
    }

    private async Task ObserveEndpointHostAsync()
    {
        try { await _endpointHost.Completion; } catch { }
        if (Volatile.Read(ref _shutdownStarted) == 0 &&
            _endpointHost.Snapshot.State == WindowsAgentEndpointHostState.Failed)
        {
            _stateStore.MarkFailed("The Windows agent endpoint listener failed.");
            _shutdownCoordinator.RequestShutdown();
        }
    }

    private void HandleBackendOwnerStateChanged(object? sender, EventArgs args)
    {
        if (_backendOwner.Snapshot.RequiresProcessRestart)
            _shutdownCoordinator.RequestShutdown();
    }

    private void HandleEndpointHostStateChanged(object? sender, EventArgs args)
    {
        if (_endpointHost.Snapshot.State == WindowsAgentEndpointHostState.Failed &&
            Volatile.Read(ref _shutdownStarted) == 0)
        {
            _stateStore.MarkFailed("The Windows agent endpoint listener failed.");
            _shutdownCoordinator.RequestShutdown();
        }
    }

    private void HandleShutdownRequested(object? sender, EventArgs args) =>
        _ = Task.Run(ObserveRequestedShutdownAsync);

    private void HandleExitRequested(object? sender, EventArgs args) =>
        _shutdownCoordinator.RequestShutdown();

    private void HandleOpenRequested(object? sender, EventArgs args) =>
        _ = OpenUiFromTrayAsync();

    private async Task ObserveRequestedShutdownAsync()
    {
        try
        {
            await ShutdownAsync();
        }
        catch
        {
            // ShutdownCoreAsync records a truthful Failed state before this observer absorbs the task failure.
        }
    }

    private async Task OpenUiFromTrayAsync()
    {
        try { await _uiOpenService.OpenAsync(UiActivationReason.TrayIcon); } catch { }
    }
}
