using PasswordManagerLocal.Windows.Ipc.Coordination;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class DelegateProcessInstanceLockFileOpener : IProcessInstanceLockFileOpener
{
    private readonly Func<string, Stream?> _open;

    public DelegateProcessInstanceLockFileOpener(Func<string, Stream?> open)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
    }

    public Stream? OpenExclusive(string lockFilePath) => _open(lockFilePath);
}
