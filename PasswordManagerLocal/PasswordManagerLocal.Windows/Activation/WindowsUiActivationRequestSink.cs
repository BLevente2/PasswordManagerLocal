using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Server;

namespace PasswordManagerLocal.Windows.Activation;

public sealed class WindowsUiActivationRequestSink : IUiActivationRequestSink
{
    private readonly IWindowsWindowActivationBridge _activationBridge;
    private readonly IWindowsUiShutdownBridge _shutdownBridge;
    private int _shutdownRequested;

    public WindowsUiActivationRequestSink(
        IWindowsWindowActivationBridge activationBridge,
        IWindowsUiShutdownBridge shutdownBridge)
    {
        _activationBridge = activationBridge ?? throw new ArgumentNullException(nameof(activationBridge));
        _shutdownBridge = shutdownBridge ?? throw new ArgumentNullException(nameof(shutdownBridge));
    }

    public async Task<bool> RequestActivationAsync(
        UiActivationRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Command != UiActivationCommand.Shutdown)
            return await _activationBridge.ActivateAsync(cancellationToken);

        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
            return true;

        try
        {
            var accepted = await _shutdownBridge.ShutdownAsync(cancellationToken);
            if (!accepted)
                Interlocked.Exchange(ref _shutdownRequested, 0);
            return accepted;
        }
        catch
        {
            Interlocked.Exchange(ref _shutdownRequested, 0);
            throw;
        }
    }
}
