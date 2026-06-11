using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace PasswordManagerLocal.Services;

public static class ClipboardService
{
    private static readonly SemaphoreSlim ClipboardSemaphore = new(1, 1);
    private static WeakReference<TopLevel>? _activeTopLevel;
    private static IClipboardWriter? _platformClipboardWriter;

    public static void SetActiveTopLevel(TopLevel? topLevel)
    {
        if (topLevel is null)
        {
            return;
        }

        _activeTopLevel = new WeakReference<TopLevel>(topLevel);
    }



    public static void SetPlatformClipboardWriter(IClipboardWriter? platformClipboardWriter)
    {
        _platformClipboardWriter = platformClipboardWriter;
    }



    public static async Task<bool> TrySetTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        await ClipboardSemaphore.WaitAsync();

        try
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                if (await TrySetTextWithPlatformClipboardAsync(text))
                {
                    return true;
                }

                if (await TrySetTextWithAvaloniaClipboardAsync(text))
                {
                    return true;
                }

                await Task.Delay(75);
            }
        }
        finally
        {
            ClipboardSemaphore.Release();
        }

        return false;
    }



    public static string? GetSelectedText(TextBox textBox) =>
        string.IsNullOrEmpty(textBox.SelectedText)
            ? null
            : textBox.SelectedText;



    private static async Task<bool> TrySetTextWithPlatformClipboardAsync(string text)
    {
        if (_platformClipboardWriter is null)
        {
            return false;
        }

        try
        {
            return await _platformClipboardWriter.TrySetTextAsync(text);
        }
        catch
        {
            return false;
        }
    }



    private static async Task<bool> TrySetTextWithAvaloniaClipboardAsync(string text)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                return await TrySetTextWithAvaloniaClipboardOnUiThreadAsync(text);
            }

            return await Dispatcher.UIThread.InvokeAsync(async () =>
                await TrySetTextWithAvaloniaClipboardOnUiThreadAsync(text));
        }
        catch
        {
            return false;
        }
    }



    private static async Task<bool> TrySetTextWithAvaloniaClipboardOnUiThreadAsync(string text)
    {
        var clipboard = GetClipboard();

        if (clipboard is null)
        {
            return false;
        }

        if (await TrySetTextWithAvaloniaSetTextAsync(clipboard, text))
        {
            return true;
        }

        return await TrySetTextWithAvaloniaDataTransferAsync(clipboard, text);
    }



    private static async Task<bool> TrySetTextWithAvaloniaSetTextAsync(IClipboard clipboard, string text)
    {
        try
        {
            await clipboard.SetTextAsync(text);
            await TryFlushAsync(clipboard);
            return true;
        }
        catch
        {
            return false;
        }
    }



    private static async Task<bool> TrySetTextWithAvaloniaDataTransferAsync(IClipboard clipboard, string text)
    {
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(data);
            await TryFlushAsync(clipboard);
            return true;
        }
        catch
        {
            return false;
        }
    }



    private static async Task TryFlushAsync(IClipboard clipboard)
    {
        try
        {
            await clipboard.FlushAsync();
        }
        catch
        {
        }
    }



    private static IClipboard? GetClipboard()
    {
        if (_activeTopLevel?.TryGetTarget(out var activeTopLevel) == true
            && activeTopLevel.Clipboard is { } activeTopLevelClipboard)
        {
            return activeTopLevelClipboard;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } desktopClipboard)
        {
            return desktopClipboard;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView
            && singleView.MainView is { } mainView
            && TopLevel.GetTopLevel(mainView) is { Clipboard: { } singleViewClipboard })
        {
            return singleViewClipboard;
        }

        return null;
    }
}
