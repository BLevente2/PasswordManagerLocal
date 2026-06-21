using System.Net;

namespace PasswordManagerLocalBackend.Services.Hosted;

internal sealed class LocalSyncHostCandidate
{
    public IPAddress Address { get; set; } = IPAddress.None;
    public int Priority { get; set; }
    public bool IsVirtualAdapter { get; set; }
}
