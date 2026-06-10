using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IDeviceService
{
    Task<bool> GetLocalDeviceSyncEnabledAsync(CancellationToken ct = default);
    Task SetLocalDeviceSyncEnabledAsync(bool isSyncOn, CancellationToken ct = default);
    Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default);
    Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default);
    Task SetUserDeviceSyncEnabledAsync(Guid token, Guid deviceId, bool isSyncEnabled, CancellationToken ct = default);
    Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default);
    Task DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default);
}
