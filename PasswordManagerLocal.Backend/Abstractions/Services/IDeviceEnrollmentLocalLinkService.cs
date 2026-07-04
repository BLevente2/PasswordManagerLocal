using PasswordManagerLocal.Backend.Abstractions.Repositories;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceEnrollmentLocalLinkService
{
    Task RemoveLocalDeviceRowsAsync(IDeviceRepository devices, CancellationToken ct = default);
    Task EnsureLocalUserDeviceAsync(
        IDeviceRepository devices,
        ILocalUserDeviceRepository localUserDevices,
        Guid userId,
        CancellationToken ct = default);
}
