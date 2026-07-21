using Avalonia;
using Avalonia.ReactiveUI;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Backend.Windows;
using PasswordManagerLocal.Frontend;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Windows.Activation;
using PasswordManagerLocal.Windows.AgentConnection;
using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Coordination;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Notifications;
using PasswordManagerLocal.Windows.SingleInstance;

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

        ClipboardService.SetPlatformClipboardWriter(new WindowsClipboardWriter());
        FirewallPermissionService.SetPlatformFirewallPermissionManager(new WindowsFirewallPermissionManager());

        var composition = WindowsBackendRuntimeFactory.Create();
        var backendClient = new InProcessFrontendBackendClient(
            composition.Runtime,
            composition.LifetimeCoordinator);
        var backgroundSyncSettingsStore = new FileBackgroundSyncSettingsStore(
            composition.ApplicationDataDirectory);
        var frontendContext = new FrontendApplicationContext(
            backendClient,
            backgroundSyncSettingsStore,
            composition.ApplicationDataDirectory);
        var activationServer = new WindowsUiActivationServer(
            names.UiActivationPipeName,
            new AvaloniaWindowActivationBridge());
        var agentConnection = new WindowsAgentControlConnection(
            names.ControlPipeName,
            new WindowsAgentLauncher(AppContext.BaseDirectory));

        try
        {
            activationServer.StartAsync().GetAwaiter().GetResult();
            var agentConnected = agentConnection.ConnectAsync().GetAwaiter().GetResult();
            if (!agentConnected)
                new WindowsStartupNotification().ShowAgentUnavailable();
            BuildAvaloniaApp(frontendContext)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            try
            {
                agentConnection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                try
                {
                    activationServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                finally
                {
                    try
                    {
                        backendClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        composition.Runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                }
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
