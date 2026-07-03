using PasswordManagerLocalBackend.Abstractions.Providers;
using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeLocalDeviceTypeProvider : ILocalDeviceTypeProvider
{
    public DeviceType DeviceType { get; set; } = DeviceType.WindowsPc;

    public DeviceType GetDeviceType() =>
        DeviceType;
}
