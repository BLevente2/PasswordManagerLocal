using PasswordManagerLocal.Windows.Ipc.Client;
using PasswordManagerLocal.Windows.Ipc.Contracts;
using PasswordManagerLocal.Windows.Ipc.Coordination;

namespace PasswordManagerLocal.Windows.SingleInstance;

public sealed class WindowsUiSingleInstanceController
{
    private readonly IProcessInstanceLock _processLock;
    private readonly IWindowsUiActivationClient _activationClient;
    private readonly int _maximumActivationAttempts;
    private readonly TimeSpan _retryDelay;

    public WindowsUiSingleInstanceController(
        IProcessInstanceLock processLock,
        IWindowsUiActivationClient activationClient,
        int maximumActivationAttempts = 3,
        TimeSpan? retryDelay = null)
    {
        _processLock = processLock ?? throw new ArgumentNullException(nameof(processLock));
        _activationClient = activationClient ?? throw new ArgumentNullException(nameof(activationClient));
        if (maximumActivationAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumActivationAttempts));
        _maximumActivationAttempts = maximumActivationAttempts;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(200);
        if (_retryDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
    }

    public async Task<WindowsUiInstanceRole> EnterAsync(
        CancellationToken cancellationToken = default)
    {
        if (_processLock.IsOwner)
        {
            _processLock.EnsureOwnership();
            return WindowsUiInstanceRole.Primary;
        }

        for (var attempt = 0; attempt < _maximumActivationAttempts; attempt++)
        {
            var result = await _activationClient.TryActivateAsync(
                new UiActivationRequestDto(
                    UiActivationReason.UserLaunch,
                    BringToForeground: true),
                cancellationToken);
            if (result.Kind != UiActivationResultKind.Unavailable)
                return WindowsUiInstanceRole.SecondaryActivationRequested;

            if (attempt + 1 < _maximumActivationAttempts)
                await Task.Delay(_retryDelay, cancellationToken);
        }

        return WindowsUiInstanceRole.SecondaryActivationUnavailable;
    }
}
