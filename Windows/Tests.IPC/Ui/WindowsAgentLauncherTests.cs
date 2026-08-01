using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Frontend.AgentConnection;
using PasswordManagerLocal.Windows.Ipc.Coordination;
using PasswordManagerLocal.Windows.Tests.IPC.Infrastructure;

namespace PasswordManagerLocal.Windows.Tests.IPC.Ui;

[TestClass]
public sealed class WindowsAgentLauncherTests
{
    [TestMethod]
    public async Task UiLaunchUsesExplicitUiRequestedArgument()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var agentDirectory = Path.Combine(
            directory,
            WindowsExecutableNames.AgentDeploymentDirectoryName);
        Directory.CreateDirectory(agentDirectory);
        var agentPath = Path.Combine(
            agentDirectory,
            WindowsExecutableNames.AgentExecutableFileName);
        await File.WriteAllBytesAsync(agentPath, Array.Empty<byte>());
        try
        {
            var processLauncher = new FakeWindowsAgentProcessLauncher();
            var launcher = new WindowsAgentLauncher(directory, processLauncher);

            Assert.IsTrue(await launcher.LaunchAsync());

            Assert.AreEqual(1, processLauncher.StartCount);
            Assert.IsNotNull(processLauncher.LastStartInfo);
            Assert.AreEqual(agentPath, processLauncher.LastStartInfo.FileName);
            Assert.AreEqual(agentDirectory, processLauncher.LastStartInfo.WorkingDirectory);
            Assert.IsFalse(processLauncher.LastStartInfo.UseShellExecute);
            CollectionAssert.AreEqual(
                new[] { WindowsAgentLaunchArguments.UiRequested },
                processLauncher.LastStartInfo.ArgumentList.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RootLevelAgentExecutableIsNotUsedAsFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(
            Path.Combine(directory, WindowsExecutableNames.AgentExecutableFileName),
            Array.Empty<byte>());
        try
        {
            var processLauncher = new FakeWindowsAgentProcessLauncher();
            var launcher = new WindowsAgentLauncher(directory, processLauncher);

            Assert.IsFalse(await launcher.LaunchAsync());
            Assert.AreEqual(0, processLauncher.StartCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
