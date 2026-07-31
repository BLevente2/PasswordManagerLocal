using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Contracts.Preferences;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Test.Fakes;

namespace PasswordManagerLocal.Test.Frontend.Settings;

[TestClass]
public sealed class UiPreferencesServiceTests
{
    [TestMethod]
    public void LanguageAndThemeChangesPersistThroughSharedStore()
    {
        var store = new InMemoryApplicationPreferencesStore(new ApplicationPreferences
        {
            SchemaVersion = ApplicationPreferences.CurrentSchemaVersion,
            Language = AppLanguage.English,
            Theme = AppThemeMode.Dark
        });
        var service = new UiPreferencesService(store);

        service.CurrentLanguage = AppLanguage.Hungarian;
        service.CurrentThemeMode = AppThemeMode.Light;
        var reloaded = new UiPreferencesService(store);

        Assert.AreEqual(AppLanguage.Hungarian, reloaded.CurrentLanguage);
        Assert.AreEqual(AppThemeMode.Light, reloaded.CurrentThemeMode);
        Assert.AreEqual(2, store.WriteCount);
    }
}
