using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PasswordManagerLocal.Services;

namespace PasswordManagerLocal.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel);
        AddHandler(
            TextBox.CopyingToClipboardEvent,
            HandleCopyingToClipboard,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        AttachedToVisualTree += (_, _) => ClipboardService.SetActiveTopLevel(TopLevel.GetTopLevel(this));
    }



    private async void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled
            || e.Key != Key.C
            || (e.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control
            || e.Source is not Control sourceControl)
        {
            return;
        }

        var textBox = sourceControl as TextBox ?? sourceControl.FindAncestorOfType<TextBox>();

        if (textBox is null)
        {
            return;
        }

        await CopySelectedTextAsync(textBox, e);
    }



    private async void HandleCopyingToClipboard(object? sender, RoutedEventArgs e)
    {
        if (e.Handled || e.Source is not TextBox textBox)
        {
            return;
        }

        await CopySelectedTextAsync(textBox, e);
    }



    private static async Task CopySelectedTextAsync(TextBox textBox, RoutedEventArgs e)
    {
        var selectedText = ClipboardService.GetSelectedText(textBox);

        if (string.IsNullOrEmpty(selectedText))
        {
            return;
        }

        e.Handled = true;
        await ClipboardService.TrySetTextAsync(selectedText);
    }
}
