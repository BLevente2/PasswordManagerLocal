namespace PasswordManagerLocalBackend.Sync.Tcp;

public sealed class PeerConnectionContext
{
    public string? RemoteIpAddress { get; init; }
    public string? ClientCertificateFingerprint { get; init; }
    public int? RemoteDatabaseVersion { get; set; }
    public bool SyncHelloAccepted { get; set; }
}
