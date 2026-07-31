using PasswordManagerLocal.Contracts.Devices;

namespace PasswordManagerLocal.Contracts.Responses;

public sealed class LocalDeviceInfoResponse
{
    public Guid DeviceId { get; set; }
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public DeviceType DeviceType { get; set; }
    public bool IsSyncOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
