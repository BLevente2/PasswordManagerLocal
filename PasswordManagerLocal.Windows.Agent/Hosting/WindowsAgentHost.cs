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
    private readonly ITrayIconController _trayIcon;
    private readonly IWindowsUiOpenService _uiOpenService;
    private readonly WindowsAgentShutdownCoordinator _shutdownCoordinator;
    private readonly WindowsAgentStateStore _stateStore;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _shutdownGate = new();
    private Task? _shutdownTask;
    private int _started;
    private int _controlServerStartupAttempted;
    private int _trayStartupAttempted;
    private int _shutdownStarted;

    public WindowsAgentHost(
        IProcessInstanceLock processLock,
        IWindowsIpcServerHost controlServer,
        ITrayIconController trayIcon,
        IWindowsUiOpenService uiOpenService,
        WindowsAgentShutdownCoordinator shutdownCoordinator,
        WindowsAgentStateStore stateStore)
    {
        _processLock = processLock ?? throw new ArgumentNullException(nameof(processLock));
        _controlServer = controlServer ?? throw new ArgumentNullException(nameof(controlServer));
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        _uiOpenService = uiOpenService ?? throw new ArgumentNullException(nameof(uiOpenService));
        _shutdownCoordinator = shutdownCoordinator ?? throw new ArgumentNullException(nameof(shutdownCoordinator));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _shutdownCoordinator.ShutdownRequested += HandleShutdownRequested;
        _trayIcon.OpenRequested += HandleOpenRequested;
        _trayIcon.ExitRequested += HandleExitRequested;
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
            Interlocked.Exchange(ref _controlServerStartupAttempted, 1);
            await _controlServer.StartAsync(cancellationToken);
            Interlocked.Exchange(ref _trayStartupAttempted, 1);
            await _trayIcon.InitializeAsync(cancellationToken);
            if (Volatile.Read(ref _shutdownStarted) == 0)
            {
                _stateStore.MarkRunning(DateTimeOffset.UtcNow);
                _ = ObserveControlServerAsync();
            }
        }
        catch (ProcessInstanceAlreadyOwnedException exception)
        {
            _processLock.Dispose();
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
            await ShutdownAsync(CancellationToken.None);
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
        await ShutdownAsync();
        _shutdownCoordinator.ShutdownRequested -= HandleShutdownRequested;
        _trayIcon.OpenRequested -= HandleOpenRequested;
        _trayIcon.ExitRequested -= HandleExitRequested;
        _lifecycleGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ShutdownCoreAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;

        await _lifecycleGate.WaitAsync();
        try
        {
            _stateStore.MarkStopping();
            if (Volatile.Read(ref _controlServerStartupAttempted) != 0)
            {
                try
                {
                    await _controlServer.StopAsync();
                }
                catch
                {
                }
            }

            if (Volatile.Read(ref _trayStartupAttempted) != 0)
            {
                try
                {
                    await _trayIcon.DisposeAsync();
                }
                catch
                {
                }
            }

            if (Volatile.Read(ref _controlServerStartupAttempted) != 0)
            {
                try
                {
                    await _controlServer.DisposeAsync();
                }
                catch
                {
                }
            }

            _processLock.Dispose();
            _stateStore.MarkStopped();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ObserveControlServerAsync()
    {
        try
        {
            await _controlServer.Completion;
        }
        catch
        {
        }

        if (Volatile.Read(ref _shutdownStarted) == 0 &&
            _controlServer.ListenerFailure is not null)
        {
            _stateStore.MarkFailed("The Windows agent control listener failed.");
            _shutdownCoordinator.RequestShutdown();
        }
    }

    private void HandleShutdownRequested(object? sender, EventArgs args) =>
        _ = ShutdownAsync();

    private void HandleExitRequested(object? sender, EventArgs args) =>
        _shutdownCoordinator.RequestShutdown();

    private void HandleOpenRequested(object? sender, EventArgs args) =>
        _ = OpenUiFromTrayAsync();

    private async Task OpenUiFromTrayAsync()
    {
        try
        {
            await _uiOpenService.OpenAsync(UiActivationReason.TrayIcon);
        }
        catch
        {
        }
    }
}
