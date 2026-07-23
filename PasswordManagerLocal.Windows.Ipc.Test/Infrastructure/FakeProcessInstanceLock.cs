using PasswordManagerLocal.Windows.Ipc.Coordination;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeProcessInstanceLock : IProcessInstanceLock
{
    private readonly FakeProcessInstanceLockState? _sharedState;
    private bool _ownsSharedState;

    public FakeProcessInstanceLock(bool isOwner = true)
    {
        IsOwner = isOwner;
    }

    public FakeProcessInstanceLock(FakeProcessInstanceLockState sharedState)
    {
        _sharedState = sharedState ?? throw new ArgumentNullException(nameof(sharedState));
        IsOwner = _ownsSharedState = sharedState.TryAcquire();
    }

    public string LockFilePath => @"C:\test\instance.lock";
    public bool IsOwner { get; }
    public bool IsDisposed { get; private set; }
    public int EnsureOwnershipCount { get; private set; }
    public Exception? EnsureOwnershipFailure { get; set; }
    public Exception? DisposeFailure { get; set; }
    public ICollection<string>? OperationLog { get; set; }

    public void EnsureOwnership()
    {
        EnsureOwnershipCount++;
        OperationLog?.Add("lock-acquire");
        if (EnsureOwnershipFailure is not null)
            throw EnsureOwnershipFailure;
        if (!IsOwner)
            throw new ProcessInstanceAlreadyOwnedException(LockFilePath);
    }

    public void Dispose()
    {
        OperationLog?.Add("lock-release");
        if (DisposeFailure is not null)
            throw DisposeFailure;
        IsDisposed = true;
        ReleaseSharedState();
    }

    public void SimulateProcessTermination() => ReleaseSharedState();

    private void ReleaseSharedState()
    {
        if (!_ownsSharedState)
            return;
        _ownsSharedState = false;
        _sharedState!.Release();
    }
}
