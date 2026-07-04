using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;

namespace PasswordManagerLocal.Backend;

public sealed class Endpoints : IEndpoints
{
    private readonly IServiceScopeFactory _scopeFactory;

    public Endpoints(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default) =>
        RunAsync<IAuthService, Guid>(service => service.RegisterAsync(request, ct));

    public Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        RunAsync<IAuthService, Guid>(service => service.LoginAsync(request, ct));

    public Task<Guid> RenewAuthSessionAsync(Guid token, CancellationToken ct = default) =>
        RunAsync<IAuthService, Guid>(service => service.RenewSessionAsync(token, ct));

    public void Logout(Guid token) =>
        Run<IAuthService>(service => service.Logout(token));

    public Task<AuthSessionStatusResponse> GetAuthSessionStatusAsync(Guid token, CancellationToken ct = default) =>
        RunAsync<IAuthService, AuthSessionStatusResponse>(service =>
            Task.FromResult(service.GetSessionStatus(token)));

    public Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default) =>
        RunAsync<IAuthService>(service => service.ChangeMasterPasswordAsync(request, ct));

    public Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default) =>
        RunAsync<IUserProfileService, UserProfileInfoResponse>(service =>
            service.GetUserProfileInfoAsync(token, ct));

    public Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default) =>
        RunAsync<IUserProfileService>(service => service.DeleteUserAccountAsync(token, password, ct));

    public Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default) =>
        RunAsync<IUserProfileService>(service => service.ChangeUsernameAsync(token, newUsername, ct));

    public Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default) =>
        RunAsync<IUserProfileService>(service => service.UpdateUserProfileInfoAsync(request, ct));

    public Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default) =>
        RunAsync<IDeviceService, LocalDeviceInfoResponse>(service => service.GetLocalDeviceInfoAsync(ct));

    public Task<bool> GetLocalUserSyncOnAsync(Guid token, CancellationToken ct = default) =>
        RunAsync<IDeviceService, bool>(service => service.GetLocalUserSyncOnAsync(token, ct));

    public Task SetLocalUserSyncOnAsync(Guid token, bool isSyncOn, CancellationToken ct = default) =>
        RunAsync<IDeviceService>(service => service.SetLocalUserSyncOnAsync(token, isSyncOn, ct));

    public Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default) =>
        RunAsync<IDeviceService>(service => service.SetLocalDeviceNameAsync(token, name, ct));

    public Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(
        Guid token,
        CancellationToken ct = default) =>
        RunAsync<IDeviceService, IReadOnlyList<UserDeviceInfoResponse>>(service =>
            service.GetUserDevicesAsync(token, ct));

    public Task SetUserDeviceNameAsync(
        Guid token,
        Guid deviceId,
        string name,
        CancellationToken ct = default) =>
        RunAsync<IDeviceService>(service => service.SetUserDeviceNameAsync(token, deviceId, name, ct));

    public Task SetUserDeviceSyncOnAsync(
        Guid token,
        Guid deviceId,
        bool isSyncOn,
        CancellationToken ct = default) =>
        RunAsync<IDeviceService>(service => service.SetUserDeviceSyncOnAsync(token, deviceId, isSyncOn, ct));

    public Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default) =>
        RunAsync<IDeviceService>(service => service.UnblockUserDeviceAsync(token, deviceId, ct));

    public Task DisconnectUserDeviceAsync(
        Guid token,
        Guid deviceId,
        byte[] masterPassword,
        CancellationToken ct = default) =>
        RunAsync<IDeviceService>(service =>
            service.DisconnectUserDeviceAsync(token, deviceId, masterPassword, ct));

    public Task<DeviceEnrollmentCodeResponse> StartDeviceEnrollmentAsync(CancellationToken ct = default) =>
        RunAsync<IDeviceEnrollmentService, DeviceEnrollmentCodeResponse>(service =>
            service.StartEnrollmentAsync(ct));

    public Task<DeviceEnrollmentStatusResponse> GetDeviceEnrollmentStatusAsync(CancellationToken ct = default) =>
        RunAsync<IDeviceEnrollmentService, DeviceEnrollmentStatusResponse>(service =>
            service.GetEnrollmentStatusAsync(ct));

    public Task CancelDeviceEnrollmentAsync(CancellationToken ct = default) =>
        RunAsync<IDeviceEnrollmentService>(service => service.CancelEnrollmentAsync(ct));

    public Task AddDeviceByCodeAsync(Guid token, string code, CancellationToken ct = default) =>
        RunAsync<IDeviceEnrollmentService>(service => service.AddDeviceByCodeAsync(token, code, ct));

    public Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default) =>
        RunAsync<IRememberMeService, IReadOnlyList<Guid>>(service =>
            service.InicializeAllRememberMeAsync(ct));

    public Task<Guid> InitializeRememberMeSessionAsync(Guid userId, CancellationToken ct = default) =>
        RunAsync<IRememberMeService, Guid>(service =>
            service.InitializeRememberMeSessionAsync(userId, ct));

    public Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default) =>
        RunAsync<IRememberMeService>(service => service.SetRememberMeAsync(token, rememberMe, ct));

    public Task<SavedPasswordsResponse> GetSavedPasswordsAsync(
        Guid token,
        CancellationToken ct = default) =>
        RunAsync<IUserPasswordsService, SavedPasswordsResponse>(service =>
            service.GetSavedPasswordsAsync(token, ct));

    public Task AddNewPasswordAsync(
        Guid token,
        NewPasswordRequest request,
        CancellationToken ct = default) =>
        RunAsync<IUserPasswordsService>(service => service.AddNewPasswordAsync(token, request, ct));

    public Task RemovePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default) =>
        RunAsync<IUserPasswordsService>(service => service.RemovePasswordAsync(token, passwordId, ct));

    public Task<byte[]> GetUnsecurePasswordAsync(
        Guid token,
        Guid passwordId,
        CancellationToken ct = default) =>
        RunAsync<IUserPasswordsService, byte[]>(service =>
            service.GetUnsecurePasswordAsync(token, passwordId, ct));

    public Task UpdatePasswordAsync(
        Guid token,
        UpdatePasswordRequest request,
        CancellationToken ct = default) =>
        RunAsync<IUserPasswordsService>(service => service.UpdatePasswordAsync(token, request, ct));

    public Task ExportPasswordsToUserAsync(
        Guid sourceToken,
        ExportPasswordsToUserRequest request,
        CancellationToken ct = default) =>
        RunAsync<IUserPasswordsService>(service => service.ExportPasswordsToUserAsync(sourceToken, request, ct));

    public Task AddCustomUserColorsAsync(
        Guid token,
        IReadOnlyList<NewCustomUserColorRequest> requests,
        CancellationToken ct = default) =>
        RunAsync<IUserCustomColorService>(service => service.AddCustomUserColorsAsync(token, requests, ct));

    public Task DeleteCustomUserColorAsync(
        Guid token,
        Guid customUserColorId,
        CancellationToken ct = default) =>
        RunAsync<IUserCustomColorService>(service => service.DeleteCustomUserColorAsync(token, customUserColorId, ct));

    public Task UpdateCustomUserColorAsync(
        Guid token,
        UpdateCustomUserColorRequest request,
        CancellationToken ct = default) =>
        RunAsync<IUserCustomColorService>(service => service.UpdateCustomUserColorAsync(token, request, ct));


    public Task AddPasswordTagAsync(
        Guid token,
        NewPasswordTagRequest request,
        CancellationToken ct = default) =>
        RunAsync<IUserPasswordTagService>(service => service.AddPasswordTagAsync(token, request, ct));

    public Task DeletePasswordTagAsync(
        Guid token,
        Guid passwordTagId,
        CancellationToken ct = default) =>
        RunAsync<IUserPasswordTagService>(service => service.DeletePasswordTagAsync(token, passwordTagId, ct));

    public Task UpdatePasswordTagAsync(
        Guid token,
        UpdatePasswordTagRequest request,
        CancellationToken ct = default) =>
        RunAsync<IUserPasswordTagService>(service => service.UpdatePasswordTagAsync(token, request, ct));

    private async Task RunAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        await action(service);
    }

    private async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(service);
    }

    private void Run<TService>(Action<TService> action)
        where TService : notnull
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        action(service);
    }
}
