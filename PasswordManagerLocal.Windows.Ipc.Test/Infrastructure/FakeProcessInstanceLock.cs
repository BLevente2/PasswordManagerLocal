using PasswordManagerLocal.Windows.Ipc.Coordination;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeProcessInstanceLock : IProcessInstanceLock
{
    public FakeProcessInstanceLock(bool isOwner = true)
    {
        IsOwner = isOwner;
    }

    public string LockFilePath => @"C:\test\instance.lock";
    public bool IsOwner { get; }
    public bool IsDisposed { get; private set; }
    public int EnsureOwnershipCount { get; private set; }
    public Exception? EnsureOwnershipFailure { get; set; }

    public void EnsureOwnership()
    {
        EnsureOwnershipCount++;
        if (EnsureOwnershipFailure is not null)
            throw EnsureOwnershipFailure;
        if (!IsOwner)
            throw new ProcessInstanceAlreadyOwnedException(LockFilePath);
    }

    public void Dispose() => IsDisposed = true;
}
