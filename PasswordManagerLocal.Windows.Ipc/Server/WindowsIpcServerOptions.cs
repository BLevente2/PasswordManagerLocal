using PasswordManagerLocal.Windows.Ipc.Protocol;

namespace PasswordManagerLocal.Windows.Ipc.Server;

public sealed class WindowsIpcServerOptions
{
    public const int DefaultMaximumActiveRequestsPerConnection = 64;

    public WindowsIpcServerOptions(
        IpcPeerRole serverRole,
        IEnumerable<IpcPeerRole> acceptedClientRoles,
        IpcCapabilities capabilities,
        int maximumActiveRequestsPerConnection = DefaultMaximumActiveRequestsPerConnection)
    {
        if (!Enum.IsDefined(serverRole) || serverRole == IpcPeerRole.TestClient)
            throw new ArgumentOutOfRangeException(nameof(serverRole));
        if (capabilities == IpcCapabilities.None || HasUnknownCapabilities(capabilities))
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        if (maximumActiveRequestsPerConnection <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumActiveRequestsPerConnection));

        ArgumentNullException.ThrowIfNull(acceptedClientRoles);
        var roles = acceptedClientRoles.ToHashSet();
        if (roles.Count == 0 || roles.Any(role => !Enum.IsDefined(role)))
            throw new ArgumentException("At least one valid client role is required.", nameof(acceptedClientRoles));

        ServerRole = serverRole;
        AcceptedClientRoles = roles;
        Capabilities = capabilities;
        MaximumActiveRequestsPerConnection = maximumActiveRequestsPerConnection;
    }

    public IpcPeerRole ServerRole { get; }
    public IReadOnlySet<IpcPeerRole> AcceptedClientRoles { get; }
    public IpcCapabilities Capabilities { get; }
    public int MaximumActiveRequestsPerConnection { get; }

    private static bool HasUnknownCapabilities(IpcCapabilities capabilities)
    {
        const IpcCapabilities known =
            IpcCapabilities.Control |
            IpcCapabilities.UiActivation |
            IpcCapabilities.Status;
        return (capabilities & ~known) != 0;
    }
}
