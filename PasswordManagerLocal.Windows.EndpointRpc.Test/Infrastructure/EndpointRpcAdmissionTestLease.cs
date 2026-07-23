namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;

internal sealed class EndpointRpcAdmissionTestLease : IDisposable
{
    private Action? _release;

    public EndpointRpcAdmissionTestLease(Action release)
    {
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
