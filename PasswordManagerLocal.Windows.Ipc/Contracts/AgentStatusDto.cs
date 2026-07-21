namespace PasswordManagerLocal.Windows.Ipc.Contracts;

public sealed record AgentStatusDto(
    AgentState AgentState,
    bool IsUiConnected,
    bool BackendOwnedByAgent,
    bool IsBackendRunning,
    bool IsBackgroundSyncEnabled,
    bool RequiresProcessRestart,
    IpcFailureDto? LastFailure,
    DateTimeOffset? StartedAtUtc);
