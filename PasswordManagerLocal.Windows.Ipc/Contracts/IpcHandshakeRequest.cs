using PasswordManagerLocal.Windows.Ipc.Protocol;

namespace PasswordManagerLocal.Windows.Ipc.Contracts;

public sealed record IpcHandshakeRequest(
    int ProtocolVersion,
    IpcPeerRole ClientRole,
    int ProcessId,
    Guid SessionId,
    IpcCapabilities Capabilities);
