
namespace PasswordManagerLocal.Backend.Sync;

public sealed class DeviceEnrollmentSnapshot
{
    public Guid PrimaryUserId { get; set; }
    public List<DeviceEnrollmentUserSnapshot> Users { get; set; } = [];
    public List<DeviceEnrollmentGroupSnapshot> Groups { get; set; } = [];
    public List<DeviceEnrollmentDeviceSnapshot> Devices { get; set; } = [];
    public List<DeviceEnrollmentUserDeviceSnapshot> UserDevices { get; set; } = [];
}

