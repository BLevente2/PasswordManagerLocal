namespace PasswordManagerLocal.Windows.Agent.Tray;

public interface ITrayIconController : IAsyncDisposable
{
    event EventHandler? OpenRequested;
    event EventHandler? ExitRequested;

    Task InitializeAsync(CancellationToken cancellationToken = default);
}
