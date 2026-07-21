using PasswordManagerLocal.Windows.Ipc.Protocol;

namespace PasswordManagerLocal.Windows.Ipc.Client;

public sealed class WindowsIpcClientOptions
{
    public const int DefaultMaximumPendingRequests = 64;

    public WindowsIpcClientOptions(
        IpcPeerRole clientRole,
        IpcPeerRole expectedServerRole,
        IpcCapabilities capabilities,
        int processId,
        Guid sessionId,
        int maximumPendingRequests = DefaultMaximumPendingRequests)
    {
        if (!Enum.IsDefined(clientRole))
            throw new ArgumentOutOfRangeException(nameof(clientRole));
        if (!Enum.IsDefined(expectedServerRole) || expectedServerRole == IpcPeerRole.TestClient)
            throw new ArgumentOutOfRangeException(nameof(expectedServerRole));
        if (capabilities == IpcCapabilities.None || HasUnknownCapabilities(capabilities))
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        if (sessionId == Guid.Empty)
            throw new ArgumentException("The IPC session ID cannot be empty.", nameof(sessionId));
        if (maximumPendingRequests <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPendingRequests));

        ClientRole = clientRole;
        ExpectedServerRole = expectedServerRole;
        Capabilities = capabilities;
        ProcessId = processId;
        SessionId = sessionId;
        MaximumPendingRequests = maximumPendingRequests;
    }

    public IpcPeerRole ClientRole { get; }
    public IpcPeerRole ExpectedServerRole { get; }
    public IpcCapabilities Capabilities { get; }
    public int ProcessId { get; }
    public Guid SessionId { get; }
    public int MaximumPendingRequests { get; }

    private static bool HasUnknownCapabilities(IpcCapabilities capabilities)
    {
        const IpcCapabilities known =
            IpcCapabilities.Control |
            IpcCapabilities.UiActivation |
            IpcCapabilities.Status;
        return (capabilities & ~known) != 0;
    }
}
