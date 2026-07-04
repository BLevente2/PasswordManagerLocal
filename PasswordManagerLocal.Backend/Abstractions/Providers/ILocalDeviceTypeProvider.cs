using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Providers;

public interface ILocalDeviceTypeProvider
{
    DeviceType GetDeviceType();
}
