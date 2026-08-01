namespace PasswordManagerLocal.Windows.Agent.Native;

internal static class WindowsNativeMessageBox
{
    internal static void ShowError(string safeMessage, nint owner = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        WindowsNativeMethods.MessageBox(
            owner,
            safeMessage,
            "PasswordManagerLocal",
            WindowsNativeMethods.MbOk |
            WindowsNativeMethods.MbIconError |
            WindowsNativeMethods.MbSetForeground);
    }
}
