using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Common.Contracts.Preferences;
using PasswordManagerLocal.Windows.Agent.Tray;

namespace PasswordManagerLocal.Windows.Tests.IPC.Ui;

[TestClass]
public sealed class WindowsAgentTrayTextTests
{
    [TestMethod]
    public void EnglishTextPreservesExistingAgentLabels()
    {
        var text = WindowsAgentTrayText.Create(AppLanguage.English);

        Assert.AreEqual("Open PasswordManagerLocal", text.OpenLabel);
        Assert.AreEqual("Exit", text.ExitLabel);
        Assert.AreEqual("The PasswordManagerLocal agent could not start.", text.StartupFailureMessage);
    }

    [TestMethod]
    public void HungarianTextPreservesUnicodeAgentLabels()
    {
        var text = WindowsAgentTrayText.Create(AppLanguage.Hungarian);

        Assert.AreEqual("PasswordManagerLocal megnyitása", text.OpenLabel);
        Assert.AreEqual("Kilépés", text.ExitLabel);
        Assert.AreEqual(
            "A PasswordManagerLocal háttérügynöke nem tudott elindulni.",
            text.StartupFailureMessage);
    }
}
