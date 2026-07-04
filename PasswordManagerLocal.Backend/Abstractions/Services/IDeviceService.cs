using PasswordManagerLocal.Backend.Responses;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceService
{
    Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default);
    Task<bool> GetLocalUserSyncOnAsync(Guid token, CancellationToken ct = default);
    Task SetLocalUserSyncOnAsync(Guid token, bool isSyncOn, CancellationToken ct = default);
    Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default);
    Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default);
    Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default);
    Task SetUserDeviceSyncOnAsync(Guid token, Guid deviceId, bool isSyncOn, CancellationToken ct = default);
    Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default);
    Task DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default);
}
