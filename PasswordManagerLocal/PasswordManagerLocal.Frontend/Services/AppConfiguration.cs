namespace PasswordManagerLocal.Frontend.Services;

public sealed class AppConfiguration
{
    public string Language { get; set; } = string.Empty;

    public string Theme { get; set; } = string.Empty;

    public bool BackgroundSyncEnabled { get; set; }

    public bool WindowsFirewallConfigured { get; set; }
}
