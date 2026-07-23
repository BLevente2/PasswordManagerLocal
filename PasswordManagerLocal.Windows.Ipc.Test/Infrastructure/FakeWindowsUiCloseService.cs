using PasswordManagerLocal.Windows.Agent.Ui;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class FakeWindowsUiCloseService : IWindowsUiCloseService
{
    public bool Result { get; set; } = true;
    public int RequestCount { get; private set; }
    public Exception? Failure { get; set; }
    public ICollection<string>? OperationLog { get; set; }

    public Task<bool> RequestCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestCount++;
        OperationLog?.Add("ui-close");
        return Failure is null
            ? Task.FromResult(Result)
            : Task.FromException<bool>(Failure);
    }
}
