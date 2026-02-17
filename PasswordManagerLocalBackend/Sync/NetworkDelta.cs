namespace PasswordManagerLocalBackend.Sync;

public sealed class NetworkDelta
{
    public string Entity { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = [];
    public long Ts { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public byte[] SignPub { get; set; } = [];
    public byte[] Sig { get; set; } = [];
}