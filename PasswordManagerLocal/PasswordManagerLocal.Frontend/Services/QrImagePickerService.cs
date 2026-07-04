using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PasswordManagerLocal.Frontend.Services;

public static class QrImagePickerService
{
    private static WeakReference<TopLevel>? _activeTopLevel;

    public static void SetActiveTopLevel(TopLevel? topLevel)
    {
        if (topLevel is null)
        {
            return;
        }

        _activeTopLevel = new WeakReference<TopLevel>(topLevel);
    }



    public static async Task<byte[]?> PickImageBytesAsync(string title, CancellationToken cancellationToken = default)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await PickImageBytesOnUiThreadAsync(title, cancellationToken);
        }

        var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await PickImageBytesOnUiThreadAsync(title, cancellationToken));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return await completion.Task;
    }



    private static async Task<byte[]?> PickImageBytesOnUiThreadAsync(string title, CancellationToken cancellationToken)
    {
        var topLevel = GetTopLevel();
        var storageProvider = topLevel?.StorageProvider;

        if (storageProvider?.CanOpen != true)
        {
            return null;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return null;
        }

        await using var input = await file.OpenReadAsync();
        using var output = new MemoryStream();
        await input.CopyToAsync(output, cancellationToken);
        return output.ToArray();
    }



    private static TopLevel? GetTopLevel()
    {
        if (_activeTopLevel?.TryGetTarget(out var activeTopLevel) == true)
        {
            return activeTopLevel;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView
            && singleView.MainView is { } mainView)
        {
            return TopLevel.GetTopLevel(mainView);
        }

        return null;
    }
}
