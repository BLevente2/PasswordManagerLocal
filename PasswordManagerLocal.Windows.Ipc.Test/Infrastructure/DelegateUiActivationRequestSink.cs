using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Ipc.Test.Infrastructure;

internal sealed class DelegateUiActivationRequestSink : IUiActivationRequestSink
{
    private readonly Func<UiActivationRequestDto, CancellationToken, Task<bool>> _callback;

    public DelegateUiActivationRequestSink(
        Func<UiActivationRequestDto, CancellationToken, Task<bool>> callback)
    {
        _callback = callback;
    }

    public Task<bool> RequestActivationAsync(
        UiActivationRequestDto request,
        CancellationToken cancellationToken) =>
        _callback(request, cancellationToken);
}
