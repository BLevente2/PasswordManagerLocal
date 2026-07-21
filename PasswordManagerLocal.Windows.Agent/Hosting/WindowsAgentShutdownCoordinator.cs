namespace PasswordManagerLocal.Windows.Agent.Hosting;

public sealed class WindowsAgentShutdownCoordinator
{
    private int _requested;

    public event EventHandler? ShutdownRequested;

    public bool RequestShutdown()
    {
        if (Interlocked.Exchange(ref _requested, 1) != 0)
            return false;

        ShutdownRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
