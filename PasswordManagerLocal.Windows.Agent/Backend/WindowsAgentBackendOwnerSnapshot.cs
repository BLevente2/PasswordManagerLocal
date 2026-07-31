using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Contracts.Runtime;
using PasswordManagerLocal.Contracts.BackgroundSync;

namespace PasswordManagerLocal.Windows.Agent.Backend;

public sealed record WindowsAgentBackendOwnerSnapshot(
    WindowsAgentBackendOwnerState State,
    BackendRuntimeSnapshot Runtime,
    InteractiveSessionLifecycleSnapshot InteractiveSession,
    SyncRuntimeSnapshot Synchronization,
    BackendLifetimeReason ActiveReasons,
    bool RequiresProcessRestart,
    bool IsResetting,
    Exception? Failure,
    DateTimeOffset ChangedAtUtc);
