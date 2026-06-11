using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PasswordManagerLocal.Localization;

namespace PasswordManagerLocal.Services;

public static class FirewallPermissionStartupPrompt
{
    public static async Task TryShowAsync(Window owner, AppLanguage language, CancellationToken ct = default)
    {
        FirewallPermissionCheckResult check;

        try
        {
            check = await FirewallPermissionService.CheckAsync(ct);
        }
        catch
        {
            return;
        }

        if (!check.IsSupported || check.IsConfigured || !check.CanRequestPermission)
            return;

        var approved = await ShowConfirmDialogAsync(owner, language);
        if (!approved)
            return;

        FirewallPermissionCheckResult result;
        try
        {
            result = await FirewallPermissionService.RequestPermissionAsync(ct);
        }
        catch (Exception ex)
        {
            result = new FirewallPermissionCheckResult
            {
                IsSupported = true,
                IsConfigured = false,
                CanRequestPermission = true,
                Details = ex.Message
            };
        }

        if (!result.IsConfigured)
            await ShowErrorDialogAsync(owner, language, result.Details);
    }



    private static async Task<bool> ShowConfirmDialogAsync(Window owner, AppLanguage language)
    {
        var result = false;
        var dialog = BuildDialog(language, "Firewall_Title", "Firewall_Message", "Firewall_Allow", "Common_Cancel", close => result = close);
        await dialog.ShowDialog(owner);
        return result;
    }



    private static async Task ShowErrorDialogAsync(Window owner, AppLanguage language, string? details)
    {
        var message = T(language, "Firewall_Failed");
        if (!string.IsNullOrWhiteSpace(details))
            message = $"{message}\n\n{details}";

        var dialog = BuildDialog(language, "Firewall_Title", message, "Common_Ok", null, _ => { }, isMessageKey: false);
        await dialog.ShowDialog(owner);
    }



    private static Window BuildDialog(
        AppLanguage language,
        string titleKey,
        string messageKeyOrText,
        string primaryButtonKey,
        string? secondaryButtonKey,
        Action<bool> setResult,
        bool isMessageKey = true)
    {
        var dialog = new Window
        {
            Title = T(language, titleKey),
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var title = new TextBlock
        {
            Text = T(language, titleKey),
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var message = new TextBlock
        {
            Text = isMessageKey ? T(language, messageKeyOrText) : messageKeyOrText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 14, 0, 0)
        };

        var primaryButton = new Button
        {
            Content = T(language, primaryButtonKey),
            MinWidth = 120,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        primaryButton.Click += (_, _) =>
        {
            setResult(true);
            dialog.Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Avalonia.Thickness(0, 24, 0, 0)
        };

        if (secondaryButtonKey is not null)
        {
            var secondaryButton = new Button
            {
                Content = T(language, secondaryButtonKey),
                MinWidth = 120,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };

            secondaryButton.Click += (_, _) =>
            {
                setResult(false);
                dialog.Close();
            };

            buttons.Children.Add(secondaryButton);
        }

        buttons.Children.Add(primaryButton);

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                title,
                message,
                buttons
            }
        };

        return dialog;
    }



    private static string T(AppLanguage language, string key) =>
        LocalizationManager.GetString(language, key);
}
