using PasswordManagerLocal.Backend.Models.Encrypted;

namespace PasswordManagerLocal.Backend.Utils;

public static class UserDeviceLoginUtil
{
    public static void UpdateCurrentDeviceLastLoginDate(
        UserDevicesData userDevicesData,
        Guid localDeviceId,
        DateTimeOffset loginTime,
        SyncVersionStamp loginVersion)
    {
        var device = userDevicesData.Devices.FirstOrDefault(device => device.Id == localDeviceId);
        if (device is null)
        {
            device = new UserDeviceData
            {
                Id = localDeviceId,
                Name = DeviceNameUtil.BuildDefaultDeviceName(localDeviceId),
                LinkedAt = loginTime,
                LastUpdatedAt = loginTime,
                Version = loginVersion
            };
            userDevicesData.Devices.Add(device);
        }

        userDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == localDeviceId);
        device.LastLoginDate = loginTime.UtcDateTime;
        device.LastUpdatedAt = loginTime;
        device.Version = loginVersion;
        device.GenerateIntegrityHash();
    }
}
