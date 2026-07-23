namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentProcessExitWaiter
{
    Task<bool> WaitForExitAsync(
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
