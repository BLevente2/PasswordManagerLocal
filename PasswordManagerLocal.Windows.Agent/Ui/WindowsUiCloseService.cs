using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.Agent.Ui;

public sealed class WindowsUiCloseService : IWindowsUiCloseService
{
    private readonly IWindowsUiActivationClient _activationClient;
    private readonly TimeSpan _acknowledgementTimeout;

    public WindowsUiCloseService(
        IWindowsUiActivationClient activationClient,
        TimeSpan? acknowledgementTimeout = null)
    {
        _activationClient = activationClient ?? throw new ArgumentNullException(nameof(activationClient));
        _acknowledgementTimeout = acknowledgementTimeout ?? TimeSpan.FromSeconds(3);
        if (_acknowledgementTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acknowledgementTimeout));
    }

    public async Task<bool> RequestCloseAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = new CancellationTokenSource(_acknowledgementTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            var result = await _activationClient.TryActivateAsync(
                new UiActivationRequestDto(
                    UiActivationReason.AgentRequest,
                    BringToForeground: false,
                    UiActivationCommand.Shutdown),
                linkedSource.Token);
            return result.Kind == UiActivationResultKind.Activated;
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
