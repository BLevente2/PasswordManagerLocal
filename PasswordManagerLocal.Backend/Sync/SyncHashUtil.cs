using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;

namespace PasswordManagerLocal.Backend.Sync;

public static class SyncHashUtil
{
    public static byte[] CalculateUserDeviceHash(UserDevice userDevice) =>
        userDevice.CalculateIntegrityHash();

    public static byte[] CalculateUserDeviceHash(UserDeviceSyncPayload payload, long timestamp) =>
        Hashing.SHA256Hash(hash =>
        {
            hash.Write(payload.UserId);
            hash.Write(payload.DeviceId);
            hash.Write(payload.IsSyncOn);
            hash.Write(payload.IsDeleted);
            hash.Write(payload.DeletedAt);
            hash.Write(timestamp);
        });
}
