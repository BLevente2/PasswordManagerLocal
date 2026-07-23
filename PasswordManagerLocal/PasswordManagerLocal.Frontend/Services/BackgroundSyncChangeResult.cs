namespace PasswordManagerLocal.Frontend.Services;

public sealed record BackgroundSyncChangeResult(
    BackgroundSyncClientState State,
    bool WasOutcomeUncertain);
