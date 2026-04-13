namespace PasswordManagerLocal.ViewModels.Pages;

public sealed class PasswordSortOptionViewModel
{
    public PasswordSortOptionViewModel(string key, string displayName)
    {
        Key = key;
        DisplayName = displayName;
    }

    public string Key { get; }

    public string DisplayName { get; }
}
