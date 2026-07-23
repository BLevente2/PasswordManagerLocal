using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Windows.Agent.Hosting;

namespace PasswordManagerLocal.Windows.Ipc.Test.Agent;

[TestClass]
public sealed class WindowsAgentCommandLineParserTests
{
    [TestMethod]
    public void EmptyCommandLineUsesNormalAgentStartup()
    {
        var options = new WindowsAgentCommandLineParser().Parse(Array.Empty<string>());

        Assert.IsFalse(options.IsBackgroundLaunch);
    }

    [TestMethod]
    public void BackgroundArgumentSelectsBackgroundLaunchWithoutChangingSettings()
    {
        var options = new WindowsAgentCommandLineParser().Parse(new[] { "--background" });

        Assert.IsTrue(options.IsBackgroundLaunch);
    }

    [TestMethod]
    public void UnknownOrCombinedArgumentsAreRejected()
    {
        var parser = new WindowsAgentCommandLineParser();

        Assert.ThrowsExactly<ArgumentException>(() => parser.Parse(new[] { "--enable-background" }));
        Assert.ThrowsExactly<ArgumentException>(() => parser.Parse(new[] { "--background", "extra" }));
    }
}
