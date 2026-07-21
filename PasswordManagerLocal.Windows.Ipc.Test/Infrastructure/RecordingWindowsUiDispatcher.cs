using PasswordManagerLocal.Windows.Activation;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class RecordingWindowsUiDispatcher : IWindowsUiDispatcher
{
    public int InvokeCount { get; private set; }
    public bool IsInvoking { get; private set; }

    public Task<T> InvokeAsync<T>(
        Func<T> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        InvokeCount++;
        IsInvoking = true;
        try
        {
            return Task.FromResult(callback());
        }
        finally
        {
            IsInvoking = false;
        }
    }
}
