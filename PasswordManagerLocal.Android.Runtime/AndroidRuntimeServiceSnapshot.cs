using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Android.Runtime;

public sealed record AndroidRuntimeServiceSnapshot(
    bool HasRuntimeComposition,
    bool HasInteractiveAttachment,
    bool HasBackgroundLease,
    bool IsForeground,
    bool IsDisposed,
    BackendLifetimeReason ActiveReasons);
