using PasswordManagerLocal.Windows.Agent.Backend;
using PasswordManagerLocal.Windows.Agent.DatabaseReset;
using PasswordManagerLocal.Windows.Agent.Endpoint;
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
        var exitCode = WindowsAgentExitCode.ShellFailure;
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
            var uiCloseService = new WindowsUiCloseService(activationClient);

            var backendOwner = new WindowsAgentBackendRuntimeOwner();
            var endpointAdapter = new AgentInteractiveEndpointAdapter(backendOwner);
            var registrationResolver = new RegisteredUiEndpointRegistrationResolver(uiCoordinator);
            var endpointHost = new WindowsAgentEndpointHost(
                names.EndpointPipeName,
                endpointAdapter,
                registrationResolver);
            var resetCoordinator = new WindowsAgentDatabaseResetCoordinator(
                endpointHost,
                backendOwner,
                shutdownCoordinator);
            var statusProvider = new WindowsAgentStatusProvider(
                stateStore,
                uiCoordinator,
                new WindowsBackgroundSyncSettingsReader(applicationDataDirectory),
                backendOwner,
                endpointHost,
                endpointAdapter,
                resetCoordinator);

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
                    new WindowsAgentExitRequestSink(shutdownCoordinator)),
                new ResetDatabaseWindowsIpcRequestHandler(resetCoordinator)
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
                endpointHost,
                backendOwner,
                trayController,
                uiOpenService,
                uiCloseService,
                uiCoordinator,
                shutdownCoordinator,
                stateStore);

            using var applicationContext = new WindowsAgentApplicationContext(host, stateStore);
            Application.Run(applicationContext);
            exitCode = applicationContext.ShellFailed
                ? WindowsAgentExitCode.ShellFailure
                : WindowsAgentExitCode.Success;
        }
        catch
        {
            ShowStartupFailure();
            exitCode = WindowsAgentExitCode.ShellFailure;
        }
        finally
        {
            if (host is not null)
            {
                try
                {
                    host.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch
                {
                    ShowShutdownFailure();
                    exitCode = WindowsAgentExitCode.ShellFailure;
                }
            }
            else
            {
                try
                {
                    processLock.Dispose();
                }
                catch
                {
                    exitCode = WindowsAgentExitCode.ShellFailure;
                }
            }
        }

        return (int)exitCode;
    }

    private static void ShowStartupFailure() =>
        MessageBox.Show(
            "The PasswordManagerLocal agent could not start.",
            "PasswordManagerLocal",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

    private static void ShowShutdownFailure() =>
        System.Diagnostics.Trace.TraceError(
            "The PasswordManagerLocal agent encountered an error during final shutdown.");
}
