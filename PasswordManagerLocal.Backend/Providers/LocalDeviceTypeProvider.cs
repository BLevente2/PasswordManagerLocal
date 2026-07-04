using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Backend.Abstractions.Providers;

namespace PasswordManagerLocal.Backend.Providers;

public sealed class LocalDeviceTypeProvider : ILocalDeviceTypeProvider
{
    public DeviceType GetDeviceType() =>
        DeviceTypeDetector.Detect();
}
