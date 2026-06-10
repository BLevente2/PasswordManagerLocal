namespace PasswordManagerLocalBackend.Responses;

public sealed class LocalDeviceInfoResponse
{
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public bool IsSyncOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
