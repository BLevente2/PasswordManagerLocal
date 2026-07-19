using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Internal.Devices;

public sealed class UserDeviceAccessor
{
    private readonly IDeviceIdentityService _identity;
    private readonly IUserDeviceRepository _userDevices;

    public UserDeviceAccessor(
        IDeviceIdentityService identity,
        IUserDeviceRepository userDevices)
    {
        _identity = identity;
        _userDevices = userDevices;
    }

    public async Task<UserDevice> GetActiveRemoteAsync(Guid userId, Guid deviceId, CancellationToken ct)
    {
        if (deviceId == Guid.Empty || deviceId == _identity.LocalDeviceId) throw new InvalidInputException();
        var userDevice = await _userDevices.GetWithDeviceAsync(userId, deviceId, ct);
        if (userDevice is null || userDevice.IsDeleted || userDevice.Device is null) throw new InvalidInputException();
        return userDevice;
    }
}
