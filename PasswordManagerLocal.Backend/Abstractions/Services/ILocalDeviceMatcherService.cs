using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface ILocalDeviceMatcherService
{
    bool IsLocalDevice(Device device);
}
