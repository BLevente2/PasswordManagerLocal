using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Agent.Hosting;

public sealed class WindowsAgentExitRequestSink : IAgentExitRequestSink
{
    private readonly WindowsAgentShutdownCoordinator _shutdownCoordinator;
    private readonly TimeSpan _responseGracePeriod;

    public WindowsAgentExitRequestSink(
        WindowsAgentShutdownCoordinator shutdownCoordinator,
        TimeSpan? responseGracePeriod = null)
    {
        _shutdownCoordinator = shutdownCoordinator
            ?? throw new ArgumentNullException(nameof(shutdownCoordinator));
        _responseGracePeriod = responseGracePeriod ?? TimeSpan.FromMilliseconds(100);
        if (_responseGracePeriod < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(responseGracePeriod));
    }

    public Task<bool> RequestExitAsync(
        AgentExitRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _ = RequestShutdownAfterResponseAsync();
        return Task.FromResult(true);
    }

    private async Task RequestShutdownAfterResponseAsync()
    {
        // The control response must be admitted before shutdown cancels active sessions.
        if (_responseGracePeriod > TimeSpan.Zero)
            await Task.Delay(_responseGracePeriod);
        _shutdownCoordinator.RequestShutdown();
    }
}
