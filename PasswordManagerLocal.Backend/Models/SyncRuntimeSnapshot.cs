namespace PasswordManagerLocal.Backend.Models;

public sealed record SyncRuntimeSnapshot(
    SyncRuntimeState State,
    Exception? Failure);
