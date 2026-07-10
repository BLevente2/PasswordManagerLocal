using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;

namespace PasswordManagerLocal.Frontend.Services;

public sealed class DeferredEndpoints : IEndpoints
{
    public async Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.RegisterAsync(FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.LoginAsync(FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task<Guid> RenewAuthSessionAsync(Guid token, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.RenewAuthSessionAsync(token, ct);
    }


    public void Logout(Guid token)
    {
        var endpoints = GetEndpoints();
        endpoints.Logout(token);
    }


    public async Task<AuthSessionStatusResponse> GetAuthSessionStatusAsync(Guid token, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.GetAuthSessionStatusAsync(token, ct);
    }


    public async Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.ChangeMasterPasswordAsync(FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.GetUserProfileInfoAsync(token, ct);
    }


    public async Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.DeleteUserAccountAsync(token, password, ct);
    }


    public async Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.ChangeUsernameAsync(token, newUsername, ct);
    }


    public async Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.UpdateUserProfileInfoAsync(FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.GetLocalDeviceInfoAsync(ct);
    }


    public async Task<bool> GetLocalUserSyncOnAsync(Guid token, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.GetLocalUserSyncOnAsync(token, ct);
    }


    public async Task SetLocalUserSyncOnAsync(Guid token, bool isSyncOn, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.SetLocalUserSyncOnAsync(token, isSyncOn, ct);
    }


    public async Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.SetLocalDeviceNameAsync(token, name, ct);
    }


    public async Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.GetUserDevicesAsync(token, ct);
    }


    public async Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.SetUserDeviceNameAsync(token, deviceId, name, ct);
    }


    public async Task SetUserDeviceSyncOnAsync(Guid token, Guid deviceId, bool isSyncOn, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.SetUserDeviceSyncOnAsync(token, deviceId, isSyncOn, ct);
    }


    public async Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.UnblockUserDeviceAsync(token, deviceId, ct);
    }


    public async Task DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.DisconnectUserDeviceAsync(token, deviceId, masterPassword, ct);
    }


    public async Task<DeviceEnrollmentCodeResponse> StartDeviceEnrollmentAsync(CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.StartDeviceEnrollmentAsync(ct);
    }


    public async Task<DeviceEnrollmentStatusResponse> GetDeviceEnrollmentStatusAsync(CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.GetDeviceEnrollmentStatusAsync(ct);
    }


    public async Task CancelDeviceEnrollmentAsync(CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.CancelDeviceEnrollmentAsync(ct);
    }


    public async Task AddDeviceByCodeAsync(Guid token, string code, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.AddDeviceByCodeAsync(token, code, ct);
    }


    public async Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.InicializeAllRememberMeAsync(ct);
    }


    public async Task<Guid> InitializeRememberMeSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.InitializeRememberMeSessionAsync(userId, ct);
    }


    public async Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.SetRememberMeAsync(token, rememberMe, ct);
    }


    public async Task<SavedPasswordsResponse> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.GetSavedPasswordsAsync(token, ct);
    }


    public async Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.AddNewPasswordAsync(token, FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task RemovePasswordsAsync(
        Guid token,
        IReadOnlyList<Guid> passwordIds,
        CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.RemovePasswordsAsync(token, passwordIds, ct);
    }


    public async Task<byte[]> GetUnsecurePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        return await endpoints.GetUnsecurePasswordAsync(token, passwordId, ct);
    }


    public async Task UpdatePasswordAsync(Guid token, UpdatePasswordRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.UpdatePasswordAsync(token, FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }

    public async Task ExportPasswordsToUserAsync(Guid sourceToken, ExportPasswordsToUserRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.ExportPasswordsToUserAsync(sourceToken, FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task AddCustomUserColorsAsync(
        Guid token,
        IReadOnlyList<NewCustomUserColorRequest> requests,
        CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.AddCustomUserColorsAsync(token, FrontendDateTimeUtil.NormalizeRequestToUtc(requests), ct);
    }


    public async Task DeleteCustomUserColorsAsync(
        Guid token,
        IReadOnlyList<Guid> customUserColorIds,
        CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.DeleteCustomUserColorsAsync(token, customUserColorIds, ct);
    }


    public async Task ExportCustomUserColorsToUserAsync(
        Guid sourceToken,
        ExportCustomUserColorsToUserRequest request,
        CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.ExportCustomUserColorsToUserAsync(sourceToken, FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task UpdateCustomUserColorAsync(Guid token, UpdateCustomUserColorRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.UpdateCustomUserColorAsync(token, FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task AddPasswordTagAsync(Guid token, NewPasswordTagRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.AddPasswordTagAsync(token, FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task DeletePasswordTagAsync(Guid token, Guid passwordTagId, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.DeletePasswordTagAsync(token, passwordTagId, ct);
    }


    public async Task ExportPasswordTagsToUserAsync(
        Guid sourceToken,
        ExportPasswordTagsToUserRequest request,
        CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.ExportPasswordTagsToUserAsync(sourceToken, FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    public async Task UpdatePasswordTagAsync(Guid token, UpdatePasswordTagRequest request, CancellationToken ct = default)
    {
        var endpoints = await GetEndpointsAsync(ct);
        await endpoints.UpdatePasswordTagAsync(token, FrontendDateTimeUtil.NormalizeRequestToUtc(request), ct);
    }


    private static async Task<IEndpoints> GetEndpointsAsync(CancellationToken ct)
    {
        await BackendHost.WaitUntilInitializedAsync(ct);
        return BackendHost.Services.GetRequiredService<IEndpoints>();
    }


    private static IEndpoints GetEndpoints()
    {
        BackendHost.WaitUntilInitializedAsync().GetAwaiter().GetResult();
        return BackendHost.Services.GetRequiredService<IEndpoints>();
    }
}
