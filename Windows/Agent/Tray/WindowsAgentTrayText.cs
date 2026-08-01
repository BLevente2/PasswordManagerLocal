using PasswordManagerLocal.Common.Contracts.Preferences;

namespace PasswordManagerLocal.Windows.Agent.Tray;

internal sealed class WindowsAgentTrayText
{
    private WindowsAgentTrayText(
        string openLabel,
        string exitLabel,
        string startupFailureMessage)
    {
        OpenLabel = openLabel;
        ExitLabel = exitLabel;
        StartupFailureMessage = startupFailureMessage;
    }

    internal string OpenLabel { get; }
    internal string ExitLabel { get; }
    internal string StartupFailureMessage { get; }
    internal string ToolTip => "PasswordManagerLocal";

    internal static WindowsAgentTrayText Create(AppLanguage language) =>
        language == AppLanguage.Hungarian
            ? new WindowsAgentTrayText(
                "PasswordManagerLocal megnyitása",
                "Kilépés",
                "A PasswordManagerLocal háttérügynöke nem tudott elindulni.")
            : new WindowsAgentTrayText(
                "Open PasswordManagerLocal",
                "Exit",
                "The PasswordManagerLocal agent could not start.");
}
