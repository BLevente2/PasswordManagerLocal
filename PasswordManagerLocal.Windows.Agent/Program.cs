using PasswordManagerLocal.Windows.Agent.Hosting;
using PasswordManagerLocal.Windows.Agent.Status;
using PasswordManagerLocal.Windows.Agent.Tray;
using PasswordManagerLocal.Windows.Agent.Ui;
using PasswordManagerLocal.Windows.Ipc.Authorization;
using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Coordination;
using PasswordManagerLocal.Windows.Ipc.Lifecycle;
using PasswordManagerLocal.Windows.Ipc.Protocol;
using PasswordManagerLocal.Windows.Ipc.Serialization;
using PasswordManagerLocal.Windows.Ipc.Server;
using PasswordManagerLocal.Windows.Ipc.Transport;
using PasswordManagerLocal.Windows.Ipc.Validation;
using System.Windows.Forms;

namespace PasswordManagerLocal.Windows.Agent;

internal sealed class Program
{
    [STAThread]
    public static int Main()
    {
        ApplicationConfiguration.Initialize();
        var applicationDataDirectory = new WindowsApplicationDataPathProvider()
            .GetApplicationDataDirectory();
        var names = new WindowsInstanceNameProvider(
            "PasswordManagerLocal",
            applicationDataDirectory,
            new WindowsUserIdentityProvider()).GetNames();

        FileProcessInstanceLock processLock;
        try
        {
            processLock = new FileProcessInstanceLock(names.AgentLockFilePath);
        }
        catch
        {
            ShowStartupFailure();
            return (int)WindowsAgentExitCode.OwnershipFailure;
        }

        if (!processLock.IsOwner)
        {
            processLock.Dispose();
            return (int)WindowsAgentExitCode.AlreadyRunning;
        }

        WindowsAgentHost? host = null;
        try
        {
            var stateStore = new WindowsAgentStateStore();
            var uiCoordinator = new SingleUiConnectionCoordinator();
            var shutdownCoordinator = new WindowsAgentShutdownCoordinator();
            var activationClient = new WindowsUiActivationClient(
                names.UiActivationPipeName,
                IpcPeerRole.Agent);
            var uiOpenService = new WindowsUiOpenService(
                activationClient,
                new WindowsUiLauncher(AppContext.BaseDirectory));
            var statusProvider = new WindowsAgentStatusProvider(
                stateStore,
                uiCoordinator,
                new WindowsBackgroundSyncSettingsReader(applicationDataDirectory));
            var serializer = new WindowsIpcSerializer();
            var validator = new WindowsIpcContractValidator();
            var handlers = new IWindowsIpcRequestHandler[]
            {
                new PingWindowsIpcRequestHandler(),
                new GetAgentStatusWindowsIpcRequestHandler(statusProvider, validator),
                new GetBackendRuntimeStatusWindowsIpcRequestHandler(statusProvider, validator),
                new GetInteractiveSessionStatusWindowsIpcRequestHandler(statusProvider, validator),
                new GetSynchronizationStatusWindowsIpcRequestHandler(statusProvider, validator),
                new RegisterUiConnectionWindowsIpcRequestHandler(uiCoordinator),
                new UnregisterUiConnectionWindowsIpcRequestHandler(uiCoordinator),
                new WindowsAgentRequestUiOpenHandler(uiOpenService),
                new WindowsAgentRequestUiActivationHandler(uiOpenService),
                new RequestAgentExitWindowsIpcRequestHandler(
                    new WindowsAgentExitRequestSink(shutdownCoordinator))
            };
            var dispatcher = new WindowsIpcRequestDispatcher(
                handlers,
                validator,
                new WindowsAgentOperationAuthorizer(
                    stateStore,
                    new WindowsIpcOperationAuthorizer(uiCoordinator)));
            var serverOptions = new WindowsIpcServerOptions(
                IpcPeerRole.Agent,
                new[] { IpcPeerRole.Ui, IpcPeerRole.TestClient },
                IpcCapabilities.Control | IpcCapabilities.Status | IpcCapabilities.UiActivation);
            var sessionFactory = new WindowsIpcServerConnectionSessionFactory(
                serializer,
                dispatcher,
                serverOptions,
                uiCoordinator: uiCoordinator,
                contractValidator: validator);
            var controlServer = new WindowsIpcServerHost(
                new WindowsNamedPipeServer(names.ControlPipeName, new IpcFrameCodec()),
                sessionFactory,
                new WindowsIpcServerHostOptions(maximumActiveConnections: 8));
            var trayController = new WindowsTrayIconController(
                new WindowsFormsTrayIconAdapter(
                    Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico")));
            host = new WindowsAgentHost(
                processLock,
                controlServer,
                trayController,
                uiOpenService,
                shutdownCoordinator,
                stateStore);

            using var applicationContext = new WindowsAgentApplicationContext(host, stateStore);
            Application.Run(applicationContext);
            return applicationContext.ShellFailed
                ? (int)WindowsAgentExitCode.ShellFailure
                : (int)WindowsAgentExitCode.Success;
        }
        catch
        {
            ShowStartupFailure();
            return (int)WindowsAgentExitCode.ShellFailure;
        }
        finally
        {
            if (host is not null)
            {
                try
                {
                    host.ShutdownAsync().GetAwaiter().GetResult();
                }
                catch
                {
                }

                try
                {
                    host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch
                {
                }
            }
            else
            {
                processLock.Dispose();
            }
        }
    }

    private static void ShowStartupFailure() =>
        MessageBox.Show(
            "The PasswordManagerLocal agent could not start.",
            "PasswordManagerLocal",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
}
