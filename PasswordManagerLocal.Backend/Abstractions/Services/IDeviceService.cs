namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IDeviceService :
    ILocalDeviceSettingsService,
    IUserDeviceQueryService,
    IUserDeviceSettingsService,
    IUserDeviceDisconnectionService
{
}
