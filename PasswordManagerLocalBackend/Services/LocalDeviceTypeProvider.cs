using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Utils;

namespace PasswordManagerLocalBackend.Services;

public sealed class LocalDeviceTypeProvider : ILocalDeviceTypeProvider
{
    public DeviceType GetDeviceType() =>
        DeviceTypeDetector.Detect();
}
