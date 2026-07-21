using PasswordManagerLocal.Windows.Ipc.Contracts;

namespace PasswordManagerLocal.Windows.Ipc.Client;

public sealed class IpcClientRequestLimitReachedException : InvalidOperationException
{
    private const string LimitMessage = "The IPC client has reached its pending-request limit.";

    public IpcClientRequestLimitReachedException(
        long correlationId,
        int maximumPendingRequests)
        : base(LimitMessage)
    {
        if (correlationId <= 0)
            throw new ArgumentOutOfRangeException(nameof(correlationId));
        if (maximumPendingRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingRequests));

        CorrelationId = correlationId;
        MaximumPendingRequests = maximumPendingRequests;
    }

    public IpcErrorCode ErrorCode => IpcErrorCode.ClientRequestLimitReached;
    public IpcErrorCategory ErrorCategory => IpcErrorCategory.Availability;
    public string SafeMessage => LimitMessage;
    public long CorrelationId { get; }
    public int MaximumPendingRequests { get; }
    public bool IsRetryable => true;
    public bool RequiresProcessRestart => false;
}
