using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using System.Text;

namespace PasswordManagerLocalBackend.Sync;

public static class SyncHashUtil
{
    public static byte[] CalculateUserDeviceHash(UserDevice userDevice) =>
        CalculateUserDeviceHash(
            userDevice.UserId,
            userDevice.DeviceId,
            userDevice.Name,
            userDevice.IsSyncEnabled,
            userDevice.IsDeleted,
            userDevice.LinkedAt,
            userDevice.DeletedAt);


    public static byte[] CalculateUserDeviceHash(UserDeviceSyncPayload payload) =>
        CalculateUserDeviceHash(
            payload.UserId,
            payload.DeviceId,
            payload.Name,
            payload.IsSyncEnabled,
            payload.IsDeleted,
            payload.LinkedAt,
            payload.DeletedAt);


    private static byte[] CalculateUserDeviceHash(
        Guid userId,
        Guid deviceId,
        string name,
        bool isSyncEnabled,
        bool isDeleted,
        DateTimeOffset linkedAt,
        DateTimeOffset? deletedAt)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(userId.ToByteArray());
        bw.Write(deviceId.ToByteArray());
        bw.Write(Encoding.UTF8.GetBytes((name ?? string.Empty).Trim()));
        bw.Write(isSyncEnabled ? (byte)1 : (byte)0);
        bw.Write(isDeleted ? (byte)1 : (byte)0);
        bw.Write(linkedAt.ToUnixTimeMilliseconds());
        bw.Write(deletedAt?.ToUnixTimeMilliseconds() ?? 0);

        return Hashing.SHA512Hash(ms.ToArray());
    }
}
