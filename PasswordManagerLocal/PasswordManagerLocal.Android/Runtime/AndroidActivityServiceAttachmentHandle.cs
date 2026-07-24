using Android.Content;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Frontend.Services;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Android;

public sealed class AndroidActivityServiceAttachmentHandle : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly Task<AndroidActivityServiceAttachment> _attachmentTask;
    private int _disposed;

    public AndroidActivityServiceAttachmentHandle(
        AndroidRuntimeServiceConnector connector,
        Context context)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(context);
        _attachmentTask = connector.AttachInteractiveClientAsync(
            context,
            _lifetimeSource.Token);
        BackendClient = new AndroidDeferredServiceFrontendBackendClient(this);
        BackgroundSyncSettingsClient = new AndroidDeferredBackgroundSyncSettingsClient(this);
    }

    public IFrontendBackendClient<IEndpoints> BackendClient { get; }
    public IBackgroundSyncSettingsClient BackgroundSyncSettingsClient { get; }

    internal Task<AndroidActivityServiceAttachment> GetAttachmentAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _attachmentTask.WaitAsync(cancellationToken);
    }

    internal bool TryGetCompletedAttachment(out AndroidActivityServiceAttachment? attachment)
    {
        if (Volatile.Read(ref _disposed) == 0 && _attachmentTask.IsCompletedSuccessfully)
        {
            attachment = _attachmentTask.Result;
            return true;
        }

        attachment = null;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetimeSource.Cancel();
        try
        {
            var attachment = await _attachmentTask;
            await attachment.DisposeAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            _lifetimeSource.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(AndroidActivityServiceAttachmentHandle));
    }
}
