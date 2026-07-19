using PasswordManagerLocal.Backend.Abstractions.Services;
using System.Collections.Concurrent;

namespace PasswordManagerLocal.Backend.Internal.Lifecycle;

internal sealed class UserLifecycleLockEntry
{
    public object ReferenceGate { get; } = new();
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public int ReferenceCount;
    public bool IsRetired;
}
