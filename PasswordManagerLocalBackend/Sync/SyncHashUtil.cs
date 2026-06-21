using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Sync;

public static class SyncHashUtil
{
    public static byte[] CalculateUserDeviceHash(UserDevice userDevice) =>
        CalculateUserDeviceHash(
            userDevice.UserId,
            userDevice.DeviceId,
            userDevice.IsSyncOn,
            userDevice.IsDeleted,
            userDevice.LinkedAt,
            userDevice.DeletedAt);


    public static byte[] CalculateUserDeviceHash(UserDeviceSyncPayload payload) =>
        CalculateUserDeviceHash(
            payload.UserId,
            payload.DeviceId,
            payload.IsSyncOn,
            payload.IsDeleted,
            payload.LinkedAt,
            payload.DeletedAt);


    private static byte[] CalculateUserDeviceHash(
        Guid userId,
        Guid deviceId,
        bool isSyncOn,
        bool isDeleted,
        DateTimeOffset linkedAt,
        DateTimeOffset? deletedAt)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(userId.ToByteArray());
        bw.Write(deviceId.ToByteArray());
        bw.Write(isSyncOn ? (byte)1 : (byte)0);
        bw.Write(isDeleted ? (byte)1 : (byte)0);
        bw.Write(linkedAt.ToUnixTimeMilliseconds());
        bw.Write(deletedAt?.ToUnixTimeMilliseconds() ?? 0);

        return Hashing.SHA512Hash(ms.ToArray());
    }
}
