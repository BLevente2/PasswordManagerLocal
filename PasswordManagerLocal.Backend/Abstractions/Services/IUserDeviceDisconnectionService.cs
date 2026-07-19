using PasswordManagerLocal.Backend.Responses;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDeviceDisconnectionService
{
    Task<DeviceRemovalResultResponse> DisconnectUserDeviceAsync(
        Guid token,
        Guid deviceId,
        byte[] masterPassword,
        CancellationToken ct = default);
}
