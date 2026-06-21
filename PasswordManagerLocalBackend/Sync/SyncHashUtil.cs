using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Sync;

public static class SyncHashUtil
{
    public static byte[] CalculateUserDeviceHash(UserDevice userDevice) =>
        userDevice.CalculateIntegrityHash();

    public static byte[] CalculateUserDeviceHash(UserDeviceSyncPayload payload, long timestamp)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(payload.UserId.ToByteArray());
        bw.Write(payload.DeviceId.ToByteArray());
        bw.Write(payload.IsSyncOn);
        bw.Write(payload.IsDeleted);
        bw.Write(payload.DeletedAt?.ToUnixTimeMilliseconds() ?? 0);
        bw.Write(timestamp);

        return Hashing.SHA512Hash(ms.ToArray());
    }
}
