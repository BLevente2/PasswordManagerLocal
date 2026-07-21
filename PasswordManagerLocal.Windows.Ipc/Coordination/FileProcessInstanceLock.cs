namespace PasswordManagerLocal.Windows.Ipc.Coordination;

public sealed class FileProcessInstanceLock : IProcessInstanceLock
{
    private Stream? _ownershipHandle;
    private int _disposeStarted;

    public FileProcessInstanceLock(
        string lockFilePath,
        IProcessInstanceLockFileOpener? fileOpener = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        if (!Path.IsPathFullyQualified(lockFilePath))
            throw new ArgumentException("The process lock file path must be absolute.", nameof(lockFilePath));

        LockFilePath = Path.GetFullPath(lockFilePath);
        var directory = Path.GetDirectoryName(LockFilePath)
            ?? throw new ArgumentException("The process lock file path has no parent directory.", nameof(lockFilePath));
        Directory.CreateDirectory(directory);

        var opener = fileOpener ?? new ProcessInstanceLockFileOpener();
        _ownershipHandle = opener.OpenExclusive(LockFilePath);
        IsOwner = _ownershipHandle is not null;
    }

    public string LockFilePath { get; }
    public bool IsOwner { get; }

    public void EnsureOwnership()
    {
        ThrowIfDisposed();
        if (!IsOwner)
            throw new ProcessInstanceAlreadyOwnedException(LockFilePath);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        Interlocked.Exchange(ref _ownershipHandle, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            throw new ObjectDisposedException(nameof(FileProcessInstanceLock));
    }
}
