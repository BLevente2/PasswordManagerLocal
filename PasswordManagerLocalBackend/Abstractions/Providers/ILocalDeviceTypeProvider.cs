using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Providers;

public interface ILocalDeviceTypeProvider
{
    DeviceType GetDeviceType();
}
