using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.AgentConnection;

public interface IWindowsAgentRegisteredConnection : IAsyncDisposable
{
    bool IsConnected { get; }
    int? AgentProcessId { get; }
    Task Completion { get; }
    Task<BackendRuntimeStatusDto> GetBackendRuntimeStatusAsync(CancellationToken cancellationToken = default);
    Task<DatabaseResetResultDto> ResetDatabaseAsync(CancellationToken cancellationToken = default);
}
