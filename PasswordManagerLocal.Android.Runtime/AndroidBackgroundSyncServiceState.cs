namespace PasswordManagerLocal.Android.Runtime;

public sealed record AndroidBackgroundSyncServiceState(
    bool IsEnabled,
    bool IsAvailable,
    bool IsDegraded,
    bool IsTransitionInProgress,
    AndroidBackgroundSyncFailureKind FailureKind,
    string? SafeMessage,
    bool IsBackgroundLeaseActive,
    bool IsForegroundActive,
    bool IsSecureStorageDeferred);
