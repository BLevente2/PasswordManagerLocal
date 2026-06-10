using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Responses;

public sealed class UserDeviceInfoResponse
{
    public Guid DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TlsCertFingerprint { get; set; } = string.Empty;
    public DateTime LastSync { get; set; }
    public DateTime LastSeen { get; set; }
    public bool IsTrusted { get; set; }
    public bool IsBlocked { get; set; }
    public string? BlockedReason { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public int InvalidSyncAttemptCount { get; set; }
    public bool IsSyncEnabled { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsCurrentDevice { get; set; }



    public static UserDeviceInfoResponse FromUserDevice(UserDevice userDevice, Guid currentDeviceId)
    {
        var device = userDevice.Device ?? throw new InvalidOperationException("Device data is missing.");

        return new UserDeviceInfoResponse
        {
            DeviceId = device.Id,
            Name = userDevice.Name,
            TlsCertFingerprint = device.TlsCertFingerprint,
            LastSync = device.LastSync,
            LastSeen = device.LastSeen,
            IsTrusted = device.IsTrusted,
            IsBlocked = device.IsBlocked,
            BlockedReason = device.BlockedReason,
            BlockedAt = device.BlockedAt,
            InvalidSyncAttemptCount = device.InvalidSyncAttemptCount,
            IsSyncEnabled = userDevice.IsSyncEnabled,
            IsDeleted = userDevice.IsDeleted,
            LinkedAt = userDevice.LinkedAt,
            DeletedAt = userDevice.DeletedAt,
            IsCurrentDevice = device.Id == currentDeviceId
        };
    }
}
