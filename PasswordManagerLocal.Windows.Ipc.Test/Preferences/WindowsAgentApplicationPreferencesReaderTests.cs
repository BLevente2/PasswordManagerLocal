using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Contracts.Preferences;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Preferences;
using PasswordManagerLocal.Windows.Agent.Preferences;

namespace PasswordManagerLocal.Windows.Ipc.Test.Preferences;

[TestClass]
public sealed class WindowsAgentApplicationPreferencesReaderTests
{
    [TestMethod]
    public async Task UiAndAgentFacingReadersResolveSameLanguageWhileAgentIgnoresTheme()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"PasswordManagerLocal-AgentPreferenceReader-{Guid.NewGuid():N}");
        try
        {
            var writer = new FileApplicationPreferencesStore(directory);
            await writer.WriteAsync(new ApplicationPreferences
            {
                SchemaVersion = ApplicationPreferences.CurrentSchemaVersion,
                Language = AppLanguage.Hungarian,
                Theme = AppThemeMode.Light
            });

            var ui = new UiPreferencesService(new FileApplicationPreferencesStore(directory));
            var agent = new WindowsAgentApplicationPreferencesReader(
                new FileApplicationPreferencesStore(directory));

            Assert.AreEqual(ui.CurrentLanguage, await agent.ReadLanguageAsync());
            Assert.AreEqual(AppThemeMode.Light, ui.CurrentThemeMode);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
