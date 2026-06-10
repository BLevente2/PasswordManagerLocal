namespace PasswordManagerLocalBackend.Sync;

public sealed class DiscoveredDeviceEndpoint
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string TlsCertFingerprint { get; init; }
}
