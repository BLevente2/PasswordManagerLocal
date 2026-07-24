using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace PasswordManagerLocal.Test.Architecture;

[TestClass]
public sealed class Phase10AndroidArchitectureGuardTests
{
    [TestMethod]
    public void ServiceCompositionIsTheOnlyAndroidRuntimeFactoryCaller()
    {
        var root = GetRepositoryRoot();
        var callers = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsGeneratedDirectory(path))
            .Where(path => !path.Contains(".Test", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "AndroidBackendRuntimeFactory.Create",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                Path.Combine(
                    "PasswordManagerLocal",
                    "PasswordManagerLocal.Android",
                    "Runtime",
                    "PasswordManagerBackgroundService.cs")
            },
            callers);
    }

    [TestMethod]
    public void ActivityAndApplicationOwnNoRuntimeOrFallbackComposition()
    {
        var root = GetRepositoryRoot();
        var activity = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android",
            "MainActivity.cs"));
        var application = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android",
            "PasswordManagerLocalApplication.cs"));
        var combined = activity + application;

        Assert.IsFalse(combined.Contains("AndroidBackendRuntimeFactory", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("BackendRuntimeLifetimeCoordinator", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("new InProcessFrontendBackendClient", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IBackendRuntime ", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("BackgroundSyncSettingsStore", StringComparison.Ordinal));
        StringAssert.Contains(activity, "AndroidActivityServiceAttachmentHandle");
        StringAssert.Contains(application, "AndroidRuntimeServiceConnector");
    }

    [TestMethod]
    public void AndroidManifestDeclaresConnectedDeviceForegroundServiceAndMinimalRestorationReceivers()
    {
        var manifestPath = Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android",
            "Properties",
            "AndroidManifest.xml");
        var document = XDocument.Load(manifestPath);
        XNamespace android = "http://schemas.android.com/apk/res/android";
        var permissions = document.Root!
            .Elements("uses-permission")
            .Select(element => element.Attribute(android + "name")?.Value)
            .ToArray();
        var service = document.Descendants("service").Single(element =>
            element.Attribute(android + "name")?.Value.EndsWith(
                "PasswordManagerBackgroundService",
                StringComparison.Ordinal) == true);
        var receiver = document.Descendants("receiver").Single(element =>
            element.Attribute(android + "name")?.Value.EndsWith(
                "AndroidBackgroundRestorationReceiver",
                StringComparison.Ordinal) == true);
        var actions = receiver.Descendants("action")
            .Select(element => element.Attribute(android + "name")?.Value)
            .ToArray();

        CollectionAssert.Contains(permissions, "android.permission.FOREGROUND_SERVICE");
        CollectionAssert.Contains(permissions, "android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE");
        CollectionAssert.Contains(permissions, "android.permission.POST_NOTIFICATIONS");
        CollectionAssert.Contains(permissions, "android.permission.RECEIVE_BOOT_COMPLETED");
        Assert.AreEqual("false", service.Attribute(android + "exported")?.Value);
        Assert.AreEqual("false", service.Attribute(android + "stopWithTask")?.Value);
        Assert.AreEqual("connectedDevice", service.Attribute(android + "foregroundServiceType")?.Value);
        Assert.AreEqual("false", receiver.Attribute(android + "exported")?.Value);
        Assert.IsFalse(permissions.Contains("android.permission.FOREGROUND_SERVICE_DATA_SYNC"));
        CollectionAssert.Contains(actions, "android.intent.action.BOOT_COMPLETED");
        CollectionAssert.Contains(actions, "android.intent.action.MY_PACKAGE_REPLACED");
        CollectionAssert.DoesNotContain(actions, "android.intent.action.USER_UNLOCKED");
        CollectionAssert.DoesNotContain(actions, "android.intent.action.LOCKED_BOOT_COMPLETED");
    }


    [TestMethod]
    public void UserUnlockedRestorationIsDynamicAndNonExported()
    {
        var root = GetRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android",
            "Runtime",
            "PasswordManagerBackgroundService.cs"));
        var manifest = File.ReadAllText(Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android",
            "Properties",
            "AndroidManifest.xml"));

        StringAssert.Contains(service, "Intent.ActionUserUnlocked");
        StringAssert.Contains(service, "ReceiverFlags.NotExported");
        StringAssert.Contains(service, "RegisterReceiver");
        StringAssert.Contains(service, "UnregisterReceiver");
        Assert.IsFalse(manifest.Contains("android.intent.action.USER_UNLOCKED", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ServiceUsesStickyRestorationButRejectsImmediateStartupFailureRestartLoops()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android",
            "Runtime",
            "PasswordManagerBackgroundService.cs"));

        StringAssert.Contains(source, "StartCommandResult.Sticky");
        StringAssert.Contains(source, "StartCommandResult.NotSticky");
        StringAssert.Contains(source, "RequestStop");
    }

    [TestMethod]
    public void NotificationTextAndIntentContainNoSensitiveRuntimeData()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android",
            "Runtime",
            "AndroidForegroundServiceController.cs"));

        StringAssert.Contains(source, "Background synchronization is active");
        StringAssert.Contains(source, "Background synchronization is waiting for device unlock");
        StringAssert.Contains(source, "typeof(MainActivity)");
        StringAssert.Contains(source, "PendingIntentFlags.Immutable");
        StringAssert.Contains(source, "GetNotificationChannel");
        StringAssert.Contains(source, "CreateNotificationChannel");
        Assert.IsFalse(source.Contains("username", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("password title", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IPAddress", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WindowsProductionProjectsDoNotReferenceAndroidRuntimeProject()
    {
        var root = GetRepositoryRoot();
        var windowsProjects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Contains("Windows", StringComparison.Ordinal))
            .Where(path => !path.Contains(".Test", StringComparison.Ordinal))
            .ToArray();

        foreach (var project in windowsProjects)
        {
            Assert.IsFalse(
                File.ReadAllText(project).Contains("PasswordManagerLocal.Android.Runtime", StringComparison.Ordinal),
                Path.GetRelativePath(root, project));
        }
    }

    [TestMethod]
    public void AndroidProjectUsesServiceRuntimeProjectWithoutFrontendFallback()
    {
        var root = GetRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "PasswordManagerLocal",
            "PasswordManagerLocal.Android",
            "PasswordManagerLocal.Android.csproj");
        var project = File.ReadAllText(projectPath);
        var androidSources = Directory.EnumerateFiles(
                Path.GetDirectoryName(projectPath)!,
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        StringAssert.Contains(project, "PasswordManagerLocal.Android.Runtime.csproj");
        Assert.IsFalse(androidSources.Any(source =>
            source.Contains("new InProcessFrontendBackendClient", StringComparison.Ordinal)));
        Assert.IsFalse(androidSources.Any(source =>
            source.Contains("StoreBackgroundSyncSettingsClient", StringComparison.Ordinal)));
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));

    private static bool ContainsGeneratedDirectory(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");
}
