using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ILocalDeviceTypeProvider
{
    DeviceType GetDeviceType();
}
