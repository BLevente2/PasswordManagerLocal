using Avalonia.Media;

namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class PasswordColorOptionViewModel
{
    public PasswordColorOptionViewModel(string key, string displayName, string hexValue)
    {
        Key = key;
        DisplayName = displayName;
        HexValue = hexValue;
        Brush = ParseBrush(hexValue);
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string HexValue { get; }

    public IBrush Brush { get; }

    private static IBrush ParseBrush(string color)
    {
        try
        {
            return global::Avalonia.Media.Brush.Parse(color);
        }
        catch
        {
            return global::Avalonia.Media.Brush.Parse("#FFFFD700");
        }
    }
}
