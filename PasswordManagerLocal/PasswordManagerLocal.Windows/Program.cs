using Avalonia;
using Avalonia.ReactiveUI;
using PasswordManagerLocal.Frontend;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Runtime.Abstractions;
using PasswordManagerLocal.Windows.Activation;
using PasswordManagerLocal.Windows.AgentConnection;
using PasswordManagerLocal.Windows.EndpointRpc.Client;
using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Coordination;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Notifications;
using PasswordManagerLocal.Windows.Settings;
using PasswordManagerLocal.Windows.SingleInstance;
using System.Diagnostics;

namespace PasswordManagerLocal.Windows;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var applicationDataDirectory = new WindowsApplicationDataPathProvider()
            .GetApplicationDataDirectory();
        var names = new WindowsInstanceNameProvider(
            "PasswordManagerLocal",
            applicationDataDirectory,
            new WindowsUserIdentityProvider()).GetNames();
        using var uiProcessLock = new FileProcessInstanceLock(names.UiLockFilePath);
        var singleInstance = new WindowsUiSingleInstanceController(
            uiProcessLock,
            new WindowsUiActivationClient(
                names.UiActivationPipeName,
                IpcPeerRole.Ui));
        var instanceRole = singleInstance
            .EnterAsync()
            .GetAwaiter()
            .GetResult();
        if (instanceRole != WindowsUiInstanceRole.Primary)
            return;

        using var process = Process.GetCurrentProcess();
        var identity = new WindowsUiIpcIdentity(
            Environment.ProcessId,
            process.SessionId,
            Guid.NewGuid());
        var agentConnection = new WindowsAgentControlConnection(
            names.ControlPipeName,
            identity,
            new WindowsAgentLauncher(AppContext.BaseDirectory));
        var backendClient = new WindowsNamedPipeFrontendBackendClient(
            agentConnection,
            new WindowsNamedPipeEndpointRpcConnector(
                names.EndpointPipeName,
                identity));
        var activationServer = new WindowsUiActivationServer(
            names.UiActivationPipeName,
            new AvaloniaWindowActivationBridge(),
            new AvaloniaUiShutdownBridge(),
            backendClient,
            () => agentConnection.AgentProcessId);
        var startupNotification = new WindowsStartupNotification();

        try
        {
            if (!agentConnection.ConnectAsync().GetAwaiter().GetResult())
            {
                startupNotification.ShowBackendUnavailable();
                return;
            }

            activationServer.StartAsync().GetAwaiter().GetResult();
            try
            {
                backendClient.ConnectAsync().GetAwaiter().GetResult();
                backendClient.WaitUntilReadyAsync().GetAwaiter().GetResult();
            }
            catch
            {
                if (backendClient.Snapshot.FailureKind != BackendRuntimeFailureKind.DatabaseCompatibility)
                {
                    startupNotification.ShowBackendUnavailable();
                    return;
                }

                // The frontend owns the existing compatibility-reset dialog and will issue the agent reset over control IPC.
            }

            ClipboardService.SetPlatformClipboardWriter(new WindowsClipboardWriter());
            FirewallPermissionService.SetPlatformFirewallPermissionManager(
                new WindowsFirewallPermissionManager());
            var frontendContext = new FrontendApplicationContext(
                backendClient,
                new WindowsPhase6BackgroundSyncSettingsStore(),
                applicationDataDirectory,
                isBackgroundSyncSettingAvailable: false);
            BuildAvaloniaApp(frontendContext)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                backendClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                activationServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp(FrontendApplicationContext frontendContext)
    {
        ArgumentNullException.ThrowIfNull(frontendContext);

        var builder = AppBuilder.Configure(() => new App(frontendContext))
            .UseWin32()
            .UseSkia()
            .WithInterFont()
            .UseReactiveUI();

#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }
}
