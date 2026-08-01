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

    public async Task<WindowsUiCloseResult> RequestIntentionalShutdownAsync(
        CancellationToken cancellationToken = default)
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
                    UiActivationCommand.IntentionalAgentShutdown),
                linkedSource.Token);
            return result.Kind switch
            {
                UiActivationResultKind.Activated => new WindowsUiCloseResult(
                    WindowsUiCloseResultKind.Acknowledged,
                    "The UI installed recovery suppression and scheduled shutdown."),
                UiActivationResultKind.Rejected => new WindowsUiCloseResult(
                    WindowsUiCloseResultKind.Rejected,
                    "The UI rejected the intentional shutdown request."),
                UiActivationResultKind.Unavailable => new WindowsUiCloseResult(
                    WindowsUiCloseResultKind.Unavailable,
                    "The registered UI activation server is unavailable."),
                _ => new WindowsUiCloseResult(
                    WindowsUiCloseResultKind.Failed,
                    "The UI did not acknowledge intentional shutdown safely.")
            };
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return new WindowsUiCloseResult(
                WindowsUiCloseResultKind.Failed,
                "The UI intentional-shutdown acknowledgement timed out.");
        }
    }
}
