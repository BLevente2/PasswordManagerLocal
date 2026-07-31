using PasswordManagerLocal.Windows.Agent.Backend;
using PasswordManagerLocal.Windows.Agent.Background;
using PasswordManagerLocal.Windows.Agent.Lifecycle;
using PasswordManagerLocal.Windows.Agent.Preferences;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Contracts.Preferences;
using PasswordManagerLocal.Preferences;
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
    public static int Main(string[] args)
    {
        WindowsAgentCommandLineOptions commandLine;
        try
        {
            commandLine = new WindowsAgentCommandLineParser().Parse(args);
        }
        catch
        {
            ShowStartupFailure();
            return (int)WindowsAgentExitCode.ShellFailure;
        }

        ApplicationConfiguration.Initialize();
        var applicationDataDirectory = new WindowsApplicationDataPathProvider()
            .GetApplicationDataDirectory();
        var names = new WindowsInstanceNameProvider(
            "PasswordManagerLocal",
            applicationDataDirectory,
            new WindowsUserIdentityProvider()).GetNames();
        var selectedLanguage = ReadSelectedLanguage(applicationDataDirectory);

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
            var admissionGate = new WindowsAgentAdmissionGate();
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
            var lifecycleTransitions = new WindowsAgentLifecycleTransitionCoordinator();
            var settingsStore = new FileBackgroundSyncSettingsStore(applicationDataDirectory);
            var processPath = Environment.ProcessPath;
            var agentExecutablePath = !string.IsNullOrWhiteSpace(processPath) &&
                string.Equals(
                    Path.GetFileName(processPath),
                    WindowsStartupRegistrationConstants.AgentExecutableFileName,
                    StringComparison.OrdinalIgnoreCase)
                ? processPath
                : Path.Combine(
                    AppContext.BaseDirectory,
                    WindowsStartupRegistrationConstants.AgentExecutableFileName);
            var startupRegistration = new WindowsRunStartupRegistration(
                new WindowsAgentStartupCommand(agentExecutablePath));
            var backgroundSyncCoordinator = new WindowsBackgroundSyncCoordinator(
                settingsStore,
                startupRegistration,
                backendOwner,
                stateStore,
                admissionGate,
                lifecycleTransitions);
            var endpointAdapter = new AgentInteractiveEndpointAdapter(backendOwner);
            var registrationResolver = new RegisteredUiEndpointRegistrationResolver(
                uiCoordinator,
                admissionGate);
            var endpointHost = new WindowsAgentEndpointHost(
                names.EndpointPipeName,
                endpointAdapter,
                registrationResolver,
                admissionGate,
                stateStore,
                backendOwner);
            var resetCoordinator = new WindowsAgentDatabaseResetCoordinator(
                endpointHost,
                backendOwner,
                shutdownCoordinator,
                backgroundSyncCoordinator,
                lifecycleTransitions);
            var statusProvider = new WindowsAgentStatusProvider(
                stateStore,
                admissionGate,
                uiCoordinator,
                backgroundSyncCoordinator,
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
                new GetBackgroundSyncStateWindowsIpcRequestHandler(
                    backgroundSyncCoordinator,
                    validator),
                new WindowsAgentAdmissionRequestHandler(
                    admissionGate,
                    new RegisterUiConnectionWindowsIpcRequestHandler(uiCoordinator)),
                new WindowsAgentAdmissionRequestHandler(
                    admissionGate,
                    new UnregisterUiConnectionWindowsIpcRequestHandler(uiCoordinator)),
                new WindowsAgentAdmissionRequestHandler(
                    admissionGate,
                    new WindowsAgentRequestUiOpenHandler(uiOpenService)),
                new WindowsAgentAdmissionRequestHandler(
                    admissionGate,
                    new WindowsAgentRequestUiActivationHandler(uiOpenService)),
                new WindowsAgentAdmissionRequestHandler(
                    admissionGate,
                    new RequestAgentExitWindowsIpcRequestHandler(
                        new WindowsAgentExitRequestSink(shutdownCoordinator))),
                new WindowsAgentAdmissionRequestHandler(
                    admissionGate,
                    new ResetDatabaseWindowsIpcRequestHandler(resetCoordinator)),
                new WindowsAgentAdmissionRequestHandler(
                    admissionGate,
                    new SetBackgroundSyncEnabledWindowsIpcRequestHandler(
                        backgroundSyncCoordinator,
                        validator))
            };
            var dispatcher = new WindowsIpcRequestDispatcher(
                handlers,
                validator,
                new WindowsAgentOperationAuthorizer(
                    stateStore,
                    admissionGate,
                    backendOwner,
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
                    Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico"),
                    selectedLanguage));
            var uiProcessLockProbe = new FileProcessInstanceLockProbe(names.UiLockFilePath);
            var processLifetimeCoordinator = new WindowsAgentProcessLifetimeCoordinator(
                commandLine.LaunchMode,
                uiCoordinator,
                backgroundSyncCoordinator,
                uiProcessLockProbe,
                shutdownCoordinator);
            host = new WindowsAgentHost(
                processLock,
                uiProcessLockProbe,
                admissionGate,
                controlServer,
                endpointHost,
                backendOwner,
                backgroundSyncCoordinator,
                lifecycleTransitions,
                trayController,
                uiOpenService,
                uiCloseService,
                uiCoordinator,
                shutdownCoordinator,
                stateStore,
                processLifetimeCoordinator: processLifetimeCoordinator);

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


    private static AppLanguage ReadSelectedLanguage(string applicationDataDirectory)
    {
        try
        {
            return new WindowsAgentApplicationPreferencesReader(
                new FileApplicationPreferencesStore(applicationDataDirectory))
                .ReadLanguageAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return ApplicationPreferencesDefaults.Create().Language;
        }
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
