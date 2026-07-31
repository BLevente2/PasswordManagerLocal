using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Activation;
using PasswordManagerLocal.Windows.Agent.Hosting;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace PasswordManagerLocal.Windows.Ipc.Test.Dependency;

[TestClass]
public sealed class Phase6DependencyBoundaryTests
{
    [TestMethod]
    public void AgentReferencesBackendCompositionAndEndpointRpcButNoFrontendOrAvalonia()
    {
        var references = typeof(WindowsAgentHost).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.IsTrue(references.Contains("PasswordManagerLocal.Backend"));
        Assert.IsTrue(references.Contains("PasswordManagerLocal.Backend.Hosting"));
        Assert.IsTrue(references.Contains("PasswordManagerLocal.Backend.Windows"));
        Assert.IsTrue(references.Contains("PasswordManagerLocal.Windows.EndpointRpc"));
        Assert.IsFalse(references.Any(name => name.StartsWith("Avalonia", StringComparison.Ordinal)));
        Assert.IsFalse(references.Contains("PasswordManagerLocal.Frontend"));
    }


    [TestMethod]
    public void AgentEndpointHostWiresTheSingleAuthoritativeConnectionLimit()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal.Windows.Agent",
            "Endpoint",
            "WindowsAgentEndpointHost.cs"));

        StringAssert.Contains(
            source,
            "new WindowsIpcServerHostOptions(MaximumActiveEndpointConnections)");
        StringAssert.Contains(
            source,
            "public const int MaximumActiveEndpointConnections = 1");
    }

    [TestMethod]
    public void WindowsUiProductionSourceHasNoBackendRuntimeOrInProcessFallback()
    {
        var root = GetRepositoryRoot();
        var windowsDirectory = Path.Combine(root, "PasswordManagerLocal", "PasswordManagerLocal.Windows");
        var source = ReadSources(windowsDirectory);
        var project = File.ReadAllText(Path.Combine(
            windowsDirectory,
            "PasswordManagerLocal.Windows.csproj"));

        Assert.IsFalse(source.Contains("WindowsBackendRuntimeFactory", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("BackendRuntimeLifetimeCoordinator", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("InProcessFrontendBackendClient", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("new BackendRuntime", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("WindowsNamedPipeFrontendBackendClient", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("WindowsNamedPipeEndpointRpcConnector", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("PasswordManagerLocal.Backend.Hosting.csproj", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("PasswordManagerLocal.Backend.Windows.csproj", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentIsOnlyWindowsProductionCallerOfRuntimeFactory()
    {
        var root = GetRepositoryRoot();
        var productionSources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".Test", StringComparison.Ordinal) &&
                !ContainsGeneratedDirectory(path))
            .ToArray();
        var callers = productionSources
            .Where(path => File.ReadAllText(path).Contains(
                "WindowsBackendRuntimeFactory.Create",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { Path.Combine("PasswordManagerLocal.Windows.Agent", "Backend", "WindowsAgentBackendRuntimeOwner.cs") },
            callers);
    }

    [TestMethod]
    public void AndroidUsesServiceOwnedInProcessRuntimeComposition()
    {
        var root = GetRepositoryRoot();
        var androidSource = ReadSources(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android"));

        Assert.IsTrue(androidSource.Contains("PasswordManagerBackgroundService", StringComparison.Ordinal));
        Assert.IsTrue(androidSource.Contains("AndroidServiceFrontendBackendClient", StringComparison.Ordinal));
        Assert.IsFalse(androidSource.Contains("new InProcessFrontendBackendClient", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsUiStartupOrdersLockIdentityControlEndpointAndFrontendContext()
    {
        var program = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Program.cs"));
        var lockIndex = program.IndexOf("names.UiLockFilePath", StringComparison.Ordinal);
        var primaryIndex = program.IndexOf("WindowsUiInstanceRole.Primary", StringComparison.Ordinal);
        var identityIndex = program.IndexOf("new WindowsUiIpcIdentity", StringComparison.Ordinal);
        var controlIndex = program.IndexOf("agentConnection.ConnectAsync", StringComparison.Ordinal);
        var endpointIndex = program.IndexOf("backendClient.ConnectAsync", StringComparison.Ordinal);
        var contextIndex = program.IndexOf("new FrontendApplicationContext", StringComparison.Ordinal);

        Assert.IsTrue(lockIndex >= 0);
        Assert.IsTrue(primaryIndex > lockIndex);
        Assert.IsTrue(identityIndex > primaryIndex);
        Assert.IsTrue(controlIndex > identityIndex);
        Assert.IsTrue(endpointIndex > controlIndex);
        Assert.IsTrue(contextIndex > endpointIndex);
    }

    [TestMethod]
    public void WindowsUiReusesOneIdentityForControlAndEndpointConnectors()
    {
        var program = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Program.cs"));

        Assert.AreEqual(1, CountOccurrences(program, "new WindowsUiIpcIdentity"));
        Assert.IsTrue(Regex.IsMatch(
            program,
            @"names\.ControlPipeName,\s*identity,",
            RegexOptions.CultureInvariant));
        Assert.IsTrue(Regex.IsMatch(
            program,
            @"names\.EndpointPipeName,\s*identity",
            RegexOptions.CultureInvariant));
        Assert.IsFalse(program.Contains("identity.ProcessId", StringComparison.Ordinal));
        Assert.IsFalse(program.Contains("identity.InstanceId", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsUiShutdownRequestsActivationStopReleasesLockAndBoundsBackendCleanup()
    {
        var root = GetRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Program.cs"));
        var shutdown = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Lifecycle",
            "WindowsUiProcessShutdownCoordinator.cs"));
        var client = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal.Windows.EndpointRpc",
            "Client",
            "WindowsNamedPipeFrontendBackendClient.cs"));

        var activationStopIndex = shutdown.IndexOf(
            "activationServer.RequestStop()",
            StringComparison.Ordinal);
        var lockDisposeIndex = shutdown.IndexOf(
            "processLock.Dispose()",
            activationStopIndex,
            StringComparison.Ordinal);
        var activationDisposeIndex = shutdown.IndexOf(
            "activationServer.DisposeAsync()",
            lockDisposeIndex,
            StringComparison.Ordinal);
        var clientDisposeIndex = shutdown.IndexOf(
            "backendClient.DisposeAsync()",
            activationDisposeIndex,
            StringComparison.Ordinal);
        var lifetimeCancelIndex = client.IndexOf(
            "_lifetimeSource.Cancel()",
            StringComparison.Ordinal);
        var endpointDisposeIndex = client.IndexOf(
            "await DisposeEndpointConnectionLockedAsync()",
            lifetimeCancelIndex,
            StringComparison.Ordinal);
        var controlDisposeIndex = client.IndexOf(
            "await _agentConnection.DisposeAsync()",
            endpointDisposeIndex,
            StringComparison.Ordinal);

        StringAssert.Contains(program, "new WindowsUiProcessShutdownCoordinator()");
        StringAssert.Contains(program, ".ShutdownAsync(");
        Assert.IsTrue(activationStopIndex >= 0);
        Assert.IsTrue(lockDisposeIndex > activationStopIndex);
        Assert.IsTrue(activationDisposeIndex > lockDisposeIndex);
        Assert.IsTrue(clientDisposeIndex > activationDisposeIndex);
        StringAssert.Contains(shutdown, "Task.WhenAny(operation, Task.Delay(timeout))");
        Assert.IsTrue(program.Contains(
            "using var uiProcessLock",
            StringComparison.Ordinal));
        Assert.IsTrue(lifetimeCancelIndex >= 0);
        Assert.IsTrue(endpointDisposeIndex > lifetimeCancelIndex);
        Assert.IsTrue(controlDisposeIndex > endpointDisposeIndex);
        Assert.IsFalse(program.Contains(
            "BackendLifetimeReason.BackgroundSync",
            StringComparison.Ordinal));
        Assert.IsFalse(program.Contains(
            "WindowsBackendRuntimeFactory",
            StringComparison.Ordinal));
        Assert.IsFalse(program.Contains(
            "new BackendRuntime",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsUiShutdownDoesNotOwnOrDisposeAgentRuntime()
    {
        var root = GetRepositoryRoot();
        var windowsDirectory = Path.Combine(root, "PasswordManagerLocal", "PasswordManagerLocal.Windows");
        var source = ReadSources(windowsDirectory);

        Assert.IsFalse(source.Contains("IBackendRuntime", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("BackendRuntimeLifetimeCoordinator", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("AcquireAsync(BackendLifetimeReason.BackgroundSync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("InProcessFrontendBackendClient", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsBackgroundSettingUsesAgentIpcWithoutDirectFileOrRegistryOwnership()
    {
        var root = GetRepositoryRoot();
        var windowsDirectory = Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows");
        var source = ReadSources(windowsDirectory);
        var program = File.ReadAllText(Path.Combine(windowsDirectory, "Program.cs"));
        var settingsView = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Frontend",
            "Views",
            "Settings",
            "SettingsView.axaml"));

        StringAssert.Contains(program, "new WindowsAgentBackgroundSyncSettingsClient(agentConnection)");
        StringAssert.Contains(source, "GetBackgroundSyncStateAsync");
        StringAssert.Contains(source, "SetBackgroundSyncEnabledAsync");
        StringAssert.Contains(settingsView, "IsBackgroundSyncToggleEnabled");
        Assert.IsFalse(source.Contains("FileBackgroundSyncSettingsStore", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Registry.CurrentUser", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("BackgroundSyncSettingsFileName", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DatabaseResetClearsSqlitePoolsAfterRuntimeDisposalAndBeforeDeletion()
    {
        var root = GetRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal.Backend.Hosting",
            "BackendRuntime.cs"));
        var cleaner = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal.Backend.Hosting",
            "BackendStorageCleaner.cs"));

        Assert.AreEqual(2, CountOccurrences(runtime, "_storageCleaner.ClearSqlitePools()"));
        Assert.AreEqual(2, CountOccurrences(runtime, "_storageCleaner.DeleteDatabaseFiles()"));
        var resetDisposeIndex = runtime.IndexOf("await DisposeCurrentHostCoreAsync()", StringComparison.Ordinal);
        var resetClearIndex = runtime.IndexOf("_storageCleaner.ClearSqlitePools()", resetDisposeIndex, StringComparison.Ordinal);
        var resetDeleteIndex = runtime.IndexOf("_storageCleaner.DeleteDatabaseFiles()", resetClearIndex, StringComparison.Ordinal);

        Assert.IsTrue(resetDisposeIndex >= 0);
        Assert.IsTrue(resetClearIndex > resetDisposeIndex);
        Assert.IsTrue(resetDeleteIndex > resetClearIndex);
        StringAssert.Contains(cleaner, "SqliteConnection.ClearAllPools()");
    }

    [TestMethod]
    public void DatabaseResetUiControlsRecoverOnSuccessAndFailure()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Frontend",
            "ViewModels",
            "MainViewModel.cs"));
        var actionIndex = source.IndexOf(
            "private async Task HandleDatabaseRecoveryPrimaryActionAsync()",
            StringComparison.Ordinal);
        var finallyIndex = source.IndexOf("finally", actionIndex, StringComparison.Ordinal);
        var resetFlagIndex = source.IndexOf(
            "_isResettingDatabase = false",
            finallyIndex,
            StringComparison.Ordinal);
        var propertyRefreshIndex = source.IndexOf(
            "RaiseDatabaseRecoveryProperties()",
            resetFlagIndex,
            StringComparison.Ordinal);

        Assert.IsTrue(actionIndex >= 0);
        Assert.IsTrue(finallyIndex > actionIndex);
        Assert.IsTrue(resetFlagIndex > finallyIndex);
        Assert.IsTrue(propertyRefreshIndex > resetFlagIndex);
    }

    [TestMethod]
    public void AgentEntryPointReturnsShellFailureWhenFinalHostDisposalFails()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal.Windows.Agent",
            "Program.cs"));
        var disposeIndex = source.IndexOf(
            "host.DisposeAsync().AsTask().GetAwaiter().GetResult()",
            StringComparison.Ordinal);
        var catchIndex = source.IndexOf("catch", disposeIndex, StringComparison.Ordinal);
        var failureExitIndex = source.IndexOf(
            "exitCode = WindowsAgentExitCode.ShellFailure",
            catchIndex,
            StringComparison.Ordinal);
        var returnIndex = source.LastIndexOf("return (int)exitCode", StringComparison.Ordinal);

        Assert.IsTrue(disposeIndex >= 0);
        Assert.IsTrue(catchIndex > disposeIndex);
        Assert.IsTrue(failureExitIndex > catchIndex);
        Assert.IsTrue(returnIndex > failureExitIndex);
        StringAssert.Contains(source, "ShowShutdownFailure();");
    }

    [TestMethod]
    public void WindowsUiAssemblyRetainsFrontendActivationAndIpcOnly()
    {
        var references = typeof(AvaloniaWindowActivationBridge).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.IsTrue(references.Contains("PasswordManagerLocal.Frontend"));
        Assert.IsTrue(references.Contains("PasswordManagerLocal.Contracts"));
        Assert.IsTrue(references.Contains("PasswordManagerLocal.Windows.EndpointRpc"));
        Assert.IsTrue(references.Contains("PasswordManagerLocal.Windows.Ipc"));
        Assert.IsFalse(references.Contains("PasswordManagerLocal.Backend.Hosting"));
        Assert.IsFalse(references.Contains("PasswordManagerLocal.Backend.Windows"));
    }


    [TestMethod]
    public void AgentIsTheOnlyWindowsProductionBackgroundSettingWriter()
    {
        var root = GetRepositoryRoot();
        var productionSources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(".Test", StringComparison.Ordinal) &&
                !ContainsGeneratedDirectory(path))
            .ToArray();
        var windowsWriters = productionSources
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}PasswordManagerLocal.Android{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "new FileBackgroundSyncSettingsStore",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { Path.Combine("PasswordManagerLocal.Windows.Agent", "Program.cs") },
            windowsWriters);
    }

    [TestMethod]
    public void ProductionStartupRegistrationUsesOnlyCurrentUserRunAndAgentExecutable()
    {
        var root = GetRepositoryRoot();
        var backgroundDirectory = Path.Combine(
            root,
            "PasswordManagerLocal.Windows.Agent",
            "Background");
        var source = ReadSources(backgroundDirectory);
        var launchArgumentsSource = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal.Windows.Ipc",
            "Coordination",
            "WindowsAgentLaunchArguments.cs"));

        StringAssert.Contains(source, "Registry.CurrentUser");
        StringAssert.Contains(source, @"Software\Microsoft\Windows\CurrentVersion\Run");
        StringAssert.Contains(source, "PasswordManagerLocal.Agent");
        StringAssert.Contains(source, "PasswordManagerLocal.Windows.Agent.exe");
        StringAssert.Contains(source, "WindowsAgentLaunchArguments.Background");
        StringAssert.Contains(
            launchArgumentsSource,
            "public const string Background = \"--background\";");
        Assert.IsFalse(source.Contains("Registry.LocalMachine", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("HKEY_LOCAL_MACHINE", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("PasswordManagerLocal.Windows.exe", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsUiBuildPublishesAgentIntoDedicatedDeploymentDirectory()
    {
        var project = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "PasswordManagerLocal.Windows.csproj"));

        StringAssert.Contains(project, "<WindowsAgentDeploymentDirectoryName>AgentRuntime\\</WindowsAgentDeploymentDirectoryName>");
        StringAssert.Contains(
            project,
            "ReferenceOutputAssembly=\"false\" Private=\"false\"");
        StringAssert.Contains(project, "@(_WindowsAgentRootBuildArtifact)");
        StringAssert.Contains(project, "$(TargetDir)PasswordManagerLocal.Windows.Agent.exe");
        StringAssert.Contains(project, "$(TargetDir)PasswordManagerLocal.Windows.Agent.deps.json");
        StringAssert.Contains(project, "$(TargetDir)PasswordManagerLocal.Windows.Agent.runtimeconfig.json");
        StringAssert.Contains(project, "Targets=\"Publish\"");
        StringAssert.Contains(project, "SelfContained=false");
        StringAssert.Contains(project, "PublishSingleFile=false");
        StringAssert.Contains(project, "PublishTrimmed=false");
        StringAssert.Contains(project, "$(TargetDir)$(WindowsAgentDeploymentDirectoryName)");
        StringAssert.Contains(project, "$(MSBuildProjectDirectory)\\Agent\\");
        StringAssert.Contains(project, "must never resolve to the UI source Agent directory");
        StringAssert.Contains(project, "$(WindowsAgentBuildDeploymentDirectory)PasswordManagerLocal.Windows.Agent.exe");
        StringAssert.Contains(project, "The complete Windows agent deployment was not produced");
        Assert.IsFalse(project.Contains("WindowsAgentBuildFile", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("WindowsAgentOutputDirectory", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("<WindowsAgentBuildDeploymentDirectory>$(OutDir)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentStartupRegistrationFallsBackToTheCurrentApphostDirectory()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal.Windows.Agent",
            "Program.cs"));

        StringAssert.Contains(source, "Environment.ProcessPath");
        StringAssert.Contains(source, "Path.GetFileName(processPath)");
        StringAssert.Contains(source, "AppContext.BaseDirectory");
        StringAssert.Contains(source, "WindowsStartupRegistrationConstants.AgentExecutableFileName");
    }

    [TestMethod]
    public void AgentOwnsExactlyOneOptionalBackgroundLeaseField()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal.Windows.Agent",
            "Background",
            "WindowsBackgroundSyncCoordinator.cs"));

        Assert.AreEqual(1, CountOccurrences(source, "IBackendRuntimeLease? _backgroundLease"));
        Assert.AreEqual(1, CountOccurrences(source, "_backendOwner.AcquireBackgroundSyncLeaseAsync"));
        Assert.IsFalse(source.Contains("static IBackendRuntimeLease", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BackgroundControlWriteUsesReadBackInsteadOfAutomaticReplay()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Settings",
            "WindowsAgentBackgroundSyncSettingsClient.cs"));

        Assert.AreEqual(1, CountOccurrences(source, "SetBackgroundSyncEnabledAsync("));
        StringAssert.Contains(source, "ReadBackAfterUncertainWriteAsync");
        StringAssert.Contains(source, "GetBackgroundSyncStateAsync");
        Assert.IsTrue(source.IndexOf(
            "GetBackgroundSyncStateAsync",
            source.IndexOf("ReadBackAfterUncertainWriteAsync", StringComparison.Ordinal),
            StringComparison.Ordinal) >= 0);
    }


    [TestMethod]
    public void WindowsUiReleasesItsInstanceLockBeforeBoundedConnectionCleanup()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Lifecycle",
            "WindowsUiProcessShutdownCoordinator.cs"));
        var activationStop = source.IndexOf(
            "activationServer.RequestStop()",
            StringComparison.Ordinal);
        var lockRelease = source.IndexOf(
            "processLock.Dispose()",
            activationStop,
            StringComparison.Ordinal);
        var activationCleanup = source.IndexOf(
            "activationServer.DisposeAsync()",
            lockRelease,
            StringComparison.Ordinal);
        var backendCleanup = source.IndexOf(
            "backendClient.DisposeAsync()",
            activationCleanup,
            StringComparison.Ordinal);

        Assert.IsTrue(activationStop >= 0);
        Assert.IsTrue(lockRelease > activationStop);
        Assert.IsTrue(activationCleanup > lockRelease);
        Assert.IsTrue(backendCleanup > activationCleanup);
        StringAssert.Contains(source, "Task.WhenAny(operation, Task.Delay(timeout))");
    }

    [TestMethod]
    public void WindowsUiMainWindowCloseExplicitlyEndsTheDesktopLifetime()
    {
        var repositoryRoot = GetRepositoryRoot();
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Frontend",
            "App.axaml.cs"));
        var programSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Program.cs"));

        StringAssert.Contains(
            appSource,
            "desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;");
        StringAssert.Contains(
            appSource,
            "mainWindow.Closed += (_, _) =>");
        StringAssert.Contains(
            appSource,
            "DesktopExitRequested?.Invoke();");
        StringAssert.Contains(
            appSource,
            "desktop.TryShutdown();");

        var closeHandler = appSource.IndexOf(
            "mainWindow.Closed += (_, _) =>",
            StringComparison.Ordinal);
        var exitRequest = appSource.IndexOf(
            "DesktopExitRequested?.Invoke();",
            StringComparison.Ordinal);
        var desktopShutdown = appSource.IndexOf(
            "TryShutdownDesktop(desktop);",
            StringComparison.Ordinal);

        Assert.IsTrue(closeHandler >= 0);
        Assert.IsTrue(exitRequest > closeHandler);
        Assert.IsTrue(desktopShutdown > exitRequest);
        StringAssert.Contains(
            programSource,
            "ShutdownMode.OnExplicitShutdown");
        StringAssert.Contains(
            programSource,
            "new WindowsUiProcessExitController(");
        StringAssert.Contains(
            programSource,
            "() => exitController.RequestExit()");
    }

    [TestMethod]
    public void WindowsUiWindowCloseStartsIndependentProcessExitBeforeAvaloniaReturns()
    {
        var repositoryRoot = GetRepositoryRoot();
        var contextSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Frontend",
            "FrontendApplicationContext.cs"));
        var appSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Frontend",
            "App.axaml.cs"));
        var programSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Program.cs"));
        var exitControllerSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Lifecycle",
            "WindowsUiProcessExitController.cs"));
        var shutdownSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Windows",
            "Lifecycle",
            "WindowsUiProcessShutdownCoordinator.cs"));

        StringAssert.Contains(contextSource, "Action? desktopExitRequested = null");
        StringAssert.Contains(contextSource, "DesktopExitRequested = desktopExitRequested;");
        StringAssert.Contains(appSource, "DesktopExitRequested?.Invoke();");
        StringAssert.Contains(programSource, "() => exitController.RequestExit()");
        StringAssert.Contains(exitControllerSource, "IsBackground = false");
        StringAssert.Contains(exitControllerSource, "_terminateProcess(exitCode);");
        StringAssert.Contains(exitControllerSource, "StopAcceptanceAndReleaseLock(");
        StringAssert.Contains(shutdownSource, "Task.Factory.StartNew(");
        Assert.IsFalse(appSource.Contains(
            "Dispatcher.UIThread.Post(() => TryShutdownDesktop(desktop))",
            StringComparison.Ordinal));
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private static string ReadSources(string directory) => string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsGeneratedDirectory(path))
            .Select(File.ReadAllText));

    private static bool ContainsGeneratedDirectory(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string GetRepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFilePath)!,
            "..",
            ".."));
}
