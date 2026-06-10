using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend;

public sealed class Endpoints : IEndpoints
{
    private readonly IAuthService _authService;
    private readonly IUserProfileService _userProfileService;
    private readonly IRememberMeService _rememberMeService;
    private readonly IUserPasswordsService _userPasswordsService;
    private readonly IDeviceService _deviceService;

    public Endpoints
        (
        IAuthService authService,
        IUserProfileService userProfileService,
        IRememberMeService rememberMeService,
        IUserPasswordsService userPasswordsService,
        IDeviceService deviceService
        )
    {
        _authService = authService;
        _userProfileService = userProfileService;
        _rememberMeService = rememberMeService;
        _userPasswordsService = userPasswordsService;
        _deviceService = deviceService;
    }





    public Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default) =>
        _authService.RegisterAsync(request, ct);


    public Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        _authService.LoginAsync(request, ct);


    public void Logout(Guid token) =>
        _authService.Logout(token);


    public Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default) =>
        _authService.ChangeMasterPasswordAsync(request, ct);




    public Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default) =>
        _userProfileService.GetUserProfileInfoAsync(token, ct);


    public Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default) =>
        _userProfileService.DeleteUserAccountAsync(token, password, ct);


    public Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default) =>
        _userProfileService.ChangeUsernameAsync(token, newUsername, ct);


    public Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default) =>
        _userProfileService.UpdateUserProfileInfoAsync(request, ct);





    public Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default) =>
        _deviceService.GetLocalDeviceInfoAsync(ct);


    public Task<bool> GetLocalDeviceSyncEnabledAsync(CancellationToken ct = default) =>
        _deviceService.GetLocalDeviceSyncEnabledAsync(ct);


    public Task SetLocalDeviceSyncEnabledAsync(bool isSyncOn, CancellationToken ct = default) =>
        _deviceService.SetLocalDeviceSyncEnabledAsync(isSyncOn, ct);


    public Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default) =>
        _deviceService.SetLocalDeviceNameAsync(token, name, ct);



    public Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default) =>
        _deviceService.GetUserDevicesAsync(token, ct);




    public Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default) =>
        _deviceService.SetUserDeviceNameAsync(token, deviceId, name, ct);


    public Task SetUserDeviceSyncEnabledAsync(Guid token, Guid deviceId, bool isSyncEnabled, CancellationToken ct = default) =>
        _deviceService.SetUserDeviceSyncEnabledAsync(token, deviceId, isSyncEnabled, ct);


    public Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default) =>
        _deviceService.UnblockUserDeviceAsync(token, deviceId, ct);


    public Task DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default) =>
        _deviceService.DisconnectUserDeviceAsync(token, deviceId, masterPassword, ct);



    public Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default) =>
        _rememberMeService.InicializeAllRememberMeAsync(ct);


    public Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default) =>
        _rememberMeService.SetRememberMeAsync(token, rememberMe, ct);




    public Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default) =>
        _userPasswordsService.GetSavedPasswordsAsync(token, ct);


    public Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default) =>
        _userPasswordsService.AddNewPasswordAsync(token, request, ct);


    public Task RemovePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default) =>
        _userPasswordsService.RemovePasswordAsync(token, passwordId, ct);


    public Task<byte[]> GetUnsecurePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default) =>
        _userPasswordsService.GetUnsecurePasswordAsync(token, passwordId, ct);

    public Task UpdatePasswordAsync(Guid token, UpdatePasswordRequest request, CancellationToken ct = default) =>
        _userPasswordsService.UpdatePasswordAsync(token, request, ct);
}