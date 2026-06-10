using PasswordManagerLocalBackend.Security;

namespace PasswordManagerLocalBackend.Sync;

public static class SyncIdentityUtil
{
    public static Guid BuildUserDeviceModelId(Guid userId, Guid deviceId)
    {
        var bytes = new byte[32];
        userId.ToByteArray().CopyTo(bytes, 0);
        deviceId.ToByteArray().CopyTo(bytes, 16);

        var hash = Hashing.SHA256Hash(bytes);
        var guidBytes = hash.Take(16).ToArray();
        return new Guid(guidBytes);
    }
}
