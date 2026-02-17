using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface IDeviceRepository : IGenericRepository<Device>
{
    Task<IReadOnlyList<Device>> ListDevicesNeedingSyncAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Device>> ListUserDevicesAsync(Guid uid, CancellationToken ct = default);
    Task<IReadOnlyList<Device>> ListGroupDevicesAsync(Guid groupId, CancellationToken ct = default);
}
