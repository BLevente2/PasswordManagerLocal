using PasswordManagerLocal.Backend.Abstractions.Providers;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeLocalDeviceTypeProvider : ILocalDeviceTypeProvider
{
    public DeviceType DeviceType { get; set; } = DeviceType.WindowsPc;

    public DeviceType GetDeviceType() =>
        DeviceType;
}
