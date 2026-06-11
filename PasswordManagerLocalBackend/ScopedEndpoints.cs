using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend;

public sealed class ScopedEndpoints : IEndpoints
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedEndpoints(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }





    public Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.RegisterAsync(request, ct));


    public Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.LoginAsync(request, ct));


    public void Logout(Guid token) =>
        Run(endpoints => endpoints.Logout(token));


    public Task<AuthSessionStatusResponse> GetAuthSessionStatusAsync(Guid token, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.GetAuthSessionStatusAsync(token, ct));


    public Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.ChangeMasterPasswordAsync(request, ct));




    public Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.GetUserProfileInfoAsync(token, ct));


    public Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.DeleteUserAccountAsync(token, password, ct));


    public Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.ChangeUsernameAsync(token, newUsername, ct));


    public Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.UpdateUserProfileInfoAsync(request, ct));




    public Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.GetLocalDeviceInfoAsync(ct));


    public Task<bool> GetLocalDeviceSyncEnabledAsync(CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.GetLocalDeviceSyncEnabledAsync(ct));


    public Task SetLocalDeviceSyncEnabledAsync(bool isSyncOn, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.SetLocalDeviceSyncEnabledAsync(isSyncOn, ct));


    public Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.SetLocalDeviceNameAsync(token, name, ct));


    public Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.GetUserDevicesAsync(token, ct));


    public Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.SetUserDeviceNameAsync(token, deviceId, name, ct));


    public Task SetUserDeviceSyncEnabledAsync(Guid token, Guid deviceId, bool isSyncEnabled, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.SetUserDeviceSyncEnabledAsync(token, deviceId, isSyncEnabled, ct));


    public Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.UnblockUserDeviceAsync(token, deviceId, ct));


    public Task DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.DisconnectUserDeviceAsync(token, deviceId, masterPassword, ct));


    public Task<DeviceEnrollmentCodeResponse> StartDeviceEnrollmentAsync(CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.StartDeviceEnrollmentAsync(ct));


    public Task<DeviceEnrollmentStatusResponse> GetDeviceEnrollmentStatusAsync(CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.GetDeviceEnrollmentStatusAsync(ct));


    public Task CancelDeviceEnrollmentAsync(CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.CancelDeviceEnrollmentAsync(ct));


    public Task AddDeviceByCodeAsync(Guid token, string code, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.AddDeviceByCodeAsync(token, code, ct));




    public Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.InicializeAllRememberMeAsync(ct));


    public Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.SetRememberMeAsync(token, rememberMe, ct));




    public Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.GetSavedPasswordsAsync(token, ct));


    public Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.AddNewPasswordAsync(token, request, ct));


    public Task RemovePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.RemovePasswordAsync(token, passwordId, ct));


    public Task<byte[]> GetUnsecurePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.GetUnsecurePasswordAsync(token, passwordId, ct));


    public Task UpdatePasswordAsync(Guid token, UpdatePasswordRequest request, CancellationToken ct = default) =>
        RunAsync(endpoints => endpoints.UpdatePasswordAsync(token, request, ct));




    private async Task RunAsync(Func<Endpoints, Task> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<Endpoints>();
        await action(endpoints);
    }


    private async Task<TResult> RunAsync<TResult>(Func<Endpoints, Task<TResult>> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<Endpoints>();
        return await action(endpoints);
    }


    private void Run(Action<Endpoints> action)
    {
        using var scope = _scopeFactory.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<Endpoints>();
        action(endpoints);
    }
}
