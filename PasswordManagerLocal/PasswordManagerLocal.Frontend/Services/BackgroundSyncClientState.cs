namespace PasswordManagerLocal.Frontend.Services;

public sealed record BackgroundSyncClientState(
    bool IsEnabled,
    bool IsAvailable,
    bool IsDegraded,
    bool IsTransitionInProgress,
    BackgroundSyncClientFailureKind FailureKind,
    string? SafeMessage);
