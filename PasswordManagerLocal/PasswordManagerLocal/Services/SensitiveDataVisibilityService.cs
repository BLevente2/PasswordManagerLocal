namespace PasswordManagerLocal.Services;

public static class SensitiveDataVisibilityService
{
    public static event EventHandler? HideVisibleSecretsRequested;

    public static void RequestHideVisibleSecrets()
    {
        HideVisibleSecretsRequested?.Invoke(null, EventArgs.Empty);
    }
}
