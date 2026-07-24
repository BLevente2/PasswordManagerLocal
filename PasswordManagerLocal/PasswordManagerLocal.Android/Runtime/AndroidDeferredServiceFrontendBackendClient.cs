using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Android;

public sealed class AndroidDeferredServiceFrontendBackendClient : IFrontendBackendClient<IEndpoints>
{
    private readonly AndroidActivityServiceAttachmentHandle _attachmentHandle;
    private readonly SemaphoreSlim _resolveLock = new(1, 1);
    private IFrontendBackendClient<IEndpoints>? _inner;
    private bool _disposed;

    public AndroidDeferredServiceFrontendBackendClient(
        AndroidActivityServiceAttachmentHandle attachmentHandle)
    {
        _attachmentHandle = attachmentHandle
            ?? throw new ArgumentNullException(nameof(attachmentHandle));
    }

    public BackendRuntimeSnapshot Snapshot
    {
        get
        {
            ThrowIfDisposed();
            if (_inner is not null)
                return _inner.Snapshot;
            if (_attachmentHandle.TryGetCompletedAttachment(out var attachment))
                return attachment!.BackendClient.Snapshot;

            return new BackendRuntimeSnapshot(
                BackendRuntimeState.NotStarted,
                BackendRuntimeFailureKind.None,
                null,
                DateTimeOffset.UtcNow);
        }
    }

    public event EventHandler<BackendRuntimeStateChangedEventArgs>? StateChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default) =>
        await (await ResolveAsync(cancellationToken)).ConnectAsync(cancellationToken);

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
        await (await ResolveAsync(cancellationToken)).WaitUntilReadyAsync(cancellationToken);

    public async Task ResetDatabaseAndRestartAsync(CancellationToken cancellationToken = default) =>
        await (await ResolveAsync(cancellationToken)).ResetDatabaseAndRestartAsync(cancellationToken);

    public async Task<IEndpoints> GetEndpointsAsync(CancellationToken cancellationToken = default) =>
        await (await ResolveAsync(cancellationToken)).GetEndpointsAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_inner is not null)
            _inner.StateChanged -= HandleInnerStateChanged;
        StateChanged = null;
        await _attachmentHandle.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task<IFrontendBackendClient<IEndpoints>> ResolveAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_inner is not null)
            return _inner;

        await _resolveLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_inner is not null)
                return _inner;

            var attachment = await _attachmentHandle.GetAttachmentAsync(cancellationToken);
            _inner = attachment.BackendClient;
            _inner.StateChanged += HandleInnerStateChanged;
            return _inner;
        }
        finally
        {
            _resolveLock.Release();
        }
    }

    private void HandleInnerStateChanged(
        object? sender,
        BackendRuntimeStateChangedEventArgs args)
    {
        var handlers = StateChanged;
        if (handlers is null)
            return;

        foreach (EventHandler<BackendRuntimeStateChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch
            {
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AndroidDeferredServiceFrontendBackendClient));
    }
}
