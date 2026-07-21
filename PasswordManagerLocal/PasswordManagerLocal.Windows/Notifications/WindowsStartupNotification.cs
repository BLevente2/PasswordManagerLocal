using System.Runtime.InteropServices;

namespace PasswordManagerLocal.Windows.Notifications;

public sealed class WindowsStartupNotification
{
    private const uint WarningIcon = 0x00000030;

    public void ShowAgentUnavailable()
    {
        _ = MessageBox(
            IntPtr.Zero,
            "PasswordManagerLocal started without the Windows agent. The password manager remains available, but tray controls are unavailable for this session.",
            "PasswordManagerLocal",
            WarningIcon);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        IntPtr windowHandle,
        string text,
        string caption,
        uint type);
}
