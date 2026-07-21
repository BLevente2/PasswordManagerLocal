namespace PasswordManagerLocal.Windows.Agent.Tray;

public sealed class WindowsTrayIconController : ITrayIconController
{
    private readonly ITrayIconAdapter _adapter;
    private int _initialized;
    private int _disposed;

    public WindowsTrayIconController(ITrayIconAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            throw new InvalidOperationException("The tray icon has already been initialized.");

        _adapter.MouseClicked += HandleMouseClicked;
        _adapter.OpenCommandSelected += HandleOpenCommandSelected;
        _adapter.ExitCommandSelected += HandleExitCommandSelected;
        try
        {
            await _adapter.InitializeAsync(cancellationToken);
        }
        catch
        {
            DetachEvents();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        DetachEvents();
        await _adapter.HideAndDisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void HandleMouseClicked(object? sender, TrayIconMouseEventArgs args)
    {
        if (args.Button == TrayIconMouseButton.Left && args.Clicks == 1)
            OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void HandleOpenCommandSelected(object? sender, EventArgs args) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void HandleExitCommandSelected(object? sender, EventArgs args) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);

    private void DetachEvents()
    {
        _adapter.MouseClicked -= HandleMouseClicked;
        _adapter.OpenCommandSelected -= HandleOpenCommandSelected;
        _adapter.ExitCommandSelected -= HandleExitCommandSelected;
    }
}
