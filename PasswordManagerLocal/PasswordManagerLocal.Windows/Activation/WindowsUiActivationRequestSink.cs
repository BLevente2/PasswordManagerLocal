using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Activation;

public sealed class WindowsUiActivationRequestSink : IUiActivationRequestSink
{
    private readonly IWindowsWindowActivationBridge _activationBridge;

    public WindowsUiActivationRequestSink(IWindowsWindowActivationBridge activationBridge)
    {
        _activationBridge = activationBridge ?? throw new ArgumentNullException(nameof(activationBridge));
    }

    public Task<bool> RequestActivationAsync(
        UiActivationRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _activationBridge.ActivateAsync(cancellationToken);
    }
}
