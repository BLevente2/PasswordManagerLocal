using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Utils;

public static class DeviceTypeDetector
{
    public static DeviceType Detect()
    {
        if (OperatingSystem.IsWindows())
            return DeviceType.WindowsPc;

        if (OperatingSystem.IsAndroid())
            return DeviceType.AndroidMobile;

        return DeviceType.Unknown;
    }

    public static bool IsValid(DeviceType deviceType) =>
        deviceType is DeviceType.WindowsPc or DeviceType.AndroidMobile;
}
