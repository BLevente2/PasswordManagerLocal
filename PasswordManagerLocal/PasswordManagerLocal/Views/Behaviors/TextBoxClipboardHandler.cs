using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PasswordManagerLocal.Services;

namespace PasswordManagerLocal.Views.Behaviors;

internal static class TextBoxClipboardHandler
{
    public static TextBox? FindSourceTextBox(Control sourceControl) =>
        sourceControl as TextBox ?? sourceControl.FindAncestorOfType<TextBox>();

    public static bool IsTextInputSource(object? source) =>
        source is Control sourceControl && FindSourceTextBox(sourceControl) is not null;

    public static async Task CopySelectedTextAsync(TextBox textBox, RoutedEventArgs e)
    {
        var selectedText = ClipboardService.GetSelectedText(textBox);
        if (string.IsNullOrEmpty(selectedText))
            return;

        e.Handled = true;
        await ClipboardService.TrySetTextAsync(selectedText);
    }

    public static async Task CutSelectedTextAsync(TextBox textBox, RoutedEventArgs e)
    {
        e.Handled = true;
        if (textBox.IsReadOnly)
            return;

        var text = textBox.Text ?? string.Empty;
        var selectionStart = Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, text.Length);
        var selectionEnd = Math.Clamp(Math.Max(textBox.SelectionStart, textBox.SelectionEnd), 0, text.Length);
        if (selectionStart >= selectionEnd)
            return;

        var selectedText = text[selectionStart..selectionEnd];
        if (!await ClipboardService.TrySetTextAsync(selectedText))
            return;

        textBox.Text = text.Remove(selectionStart, selectionEnd - selectionStart);
        textBox.CaretIndex = selectionStart;
        textBox.SelectionStart = selectionStart;
        textBox.SelectionEnd = selectionStart;
    }
}
