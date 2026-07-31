namespace PasswordManagerLocal.Contracts.BackgroundSync;

public sealed record BackgroundSyncChangeResult(
    BackgroundSyncClientState State,
    bool WasOutcomeUncertain);
