using PasswordManagerLocal.Contracts.Responses;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDeviceQueryService
{
    Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default);
}
