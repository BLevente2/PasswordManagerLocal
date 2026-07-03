using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Utils;
using PasswordManagerLocalBackend.Abstractions.Providers;

namespace PasswordManagerLocalBackend.Providers;

public sealed class LocalDeviceTypeProvider : ILocalDeviceTypeProvider
{
    public DeviceType GetDeviceType() =>
        DeviceTypeDetector.Detect();
}
