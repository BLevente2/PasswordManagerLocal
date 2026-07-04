namespace PasswordManagerLocal.Frontend.Services;

public static class SoftwareKeyboardService
{
    private static Action? _hidePlatformKeyboard;

    public static void SetPlatformHideAction(Action? hidePlatformKeyboard) =>
        _hidePlatformKeyboard = hidePlatformKeyboard;

    public static void Hide()
    {
        try
        {
            _hidePlatformKeyboard?.Invoke();
        }
        catch
        {
        }
    }
}
