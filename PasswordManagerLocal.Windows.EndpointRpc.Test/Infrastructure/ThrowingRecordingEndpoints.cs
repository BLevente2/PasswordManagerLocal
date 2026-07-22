using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Requests;
using PasswordManagerLocal.Backend.Responses;

namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;

public class ThrowingRecordingEndpoints : IEndpoints
{
    public string? LastMethodName { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    private Task Throw(string methodName, CancellationToken cancellationToken)
    {
        LastMethodName = methodName;
        LastCancellationToken = cancellationToken;
        return Task.FromException(new TestEndpointInvocationException());
    }

    private Task<T> Throw<T>(string methodName, CancellationToken cancellationToken)
    {
        LastMethodName = methodName;
        LastCancellationToken = cancellationToken;
        return Task.FromException<T>(new TestEndpointInvocationException());
    }

    public Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default) => Throw<Guid>(nameof(RegisterAsync), ct);
    public Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default) => Throw<Guid>(nameof(LoginAsync), ct);
    public Task<Guid> RenewAuthSessionAsync(Guid token, CancellationToken ct = default) => Throw<Guid>(nameof(RenewAuthSessionAsync), ct);
    public virtual Task LogoutAsync(Guid token, CancellationToken ct = default) => Throw(nameof(LogoutAsync), ct);
    public Task<AuthSessionStatusResponse> GetAuthSessionStatusAsync(Guid token, CancellationToken ct = default) => Throw<AuthSessionStatusResponse>(nameof(GetAuthSessionStatusAsync), ct);
    public Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default) => Throw(nameof(ChangeMasterPasswordAsync), ct);
    public Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default) => Throw<UserProfileInfoResponse>(nameof(GetUserProfileInfoAsync), ct);
    public Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default) => Throw(nameof(DeleteUserAccountAsync), ct);
    public Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default) => Throw(nameof(ChangeUsernameAsync), ct);
    public Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default) => Throw(nameof(UpdateUserProfileInfoAsync), ct);
    public Task<LocalDeviceInfoResponse> GetLocalDeviceInfoAsync(CancellationToken ct = default) => Throw<LocalDeviceInfoResponse>(nameof(GetLocalDeviceInfoAsync), ct);
    public Task<bool> GetLocalUserSyncOnAsync(Guid token, CancellationToken ct = default) => Throw<bool>(nameof(GetLocalUserSyncOnAsync), ct);
    public Task SetLocalUserSyncOnAsync(Guid token, bool isSyncOn, CancellationToken ct = default) => Throw(nameof(SetLocalUserSyncOnAsync), ct);
    public Task SetLocalDeviceNameAsync(Guid token, string name, CancellationToken ct = default) => Throw(nameof(SetLocalDeviceNameAsync), ct);
    public Task<IReadOnlyList<UserDeviceInfoResponse>> GetUserDevicesAsync(Guid token, CancellationToken ct = default) => Throw<IReadOnlyList<UserDeviceInfoResponse>>(nameof(GetUserDevicesAsync), ct);
    public Task SetUserDeviceNameAsync(Guid token, Guid deviceId, string name, CancellationToken ct = default) => Throw(nameof(SetUserDeviceNameAsync), ct);
    public Task SetUserDeviceSyncOnAsync(Guid token, Guid deviceId, bool isSyncOn, CancellationToken ct = default) => Throw(nameof(SetUserDeviceSyncOnAsync), ct);
    public Task UnblockUserDeviceAsync(Guid token, Guid deviceId, CancellationToken ct = default) => Throw(nameof(UnblockUserDeviceAsync), ct);
    public Task<DeviceRemovalResultResponse> DisconnectUserDeviceAsync(Guid token, Guid deviceId, byte[] masterPassword, CancellationToken ct = default) => Throw<DeviceRemovalResultResponse>(nameof(DisconnectUserDeviceAsync), ct);
    public Task<DeviceEnrollmentCodeResponse> StartDeviceEnrollmentAsync(CancellationToken ct = default) => Throw<DeviceEnrollmentCodeResponse>(nameof(StartDeviceEnrollmentAsync), ct);
    public Task<DeviceEnrollmentStatusResponse> GetDeviceEnrollmentStatusAsync(CancellationToken ct = default) => Throw<DeviceEnrollmentStatusResponse>(nameof(GetDeviceEnrollmentStatusAsync), ct);
    public Task CancelDeviceEnrollmentAsync(CancellationToken ct = default) => Throw(nameof(CancelDeviceEnrollmentAsync), ct);
    public Task AddDeviceByCodeAsync(Guid token, string code, CancellationToken ct = default) => Throw(nameof(AddDeviceByCodeAsync), ct);
    public Task<IReadOnlyList<Guid>> RestoreRememberedSessionsAsync(CancellationToken ct = default) => Throw<IReadOnlyList<Guid>>(nameof(RestoreRememberedSessionsAsync), ct);
    public Task<Guid> InitializeRememberMeSessionAsync(Guid userId, CancellationToken ct = default) => Throw<Guid>(nameof(InitializeRememberMeSessionAsync), ct);
    public Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default) => Throw(nameof(SetRememberMeAsync), ct);
    public virtual Task<SavedPasswordsResponse> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default) => Throw<SavedPasswordsResponse>(nameof(GetSavedPasswordsAsync), ct);
    public Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default) => Throw(nameof(AddNewPasswordAsync), ct);
    public Task RemovePasswordsAsync(Guid token, IReadOnlyList<Guid> passwordIds, CancellationToken ct = default) => Throw(nameof(RemovePasswordsAsync), ct);
    public virtual Task<byte[]> GetUnsecurePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default) => Throw<byte[]>(nameof(GetUnsecurePasswordAsync), ct);
    public Task UpdatePasswordAsync(Guid token, UpdatePasswordRequest request, CancellationToken ct = default) => Throw(nameof(UpdatePasswordAsync), ct);
    public Task ExportPasswordsToUserAsync(Guid sourceToken, ExportPasswordsToUserRequest request, CancellationToken ct = default) => Throw(nameof(ExportPasswordsToUserAsync), ct);
    public Task AddCustomUserColorsAsync(Guid token, IReadOnlyList<NewCustomUserColorRequest> requests, CancellationToken ct = default) => Throw(nameof(AddCustomUserColorsAsync), ct);
    public Task DeleteCustomUserColorsAsync(Guid token, IReadOnlyList<Guid> customUserColorIds, CancellationToken ct = default) => Throw(nameof(DeleteCustomUserColorsAsync), ct);
    public Task ExportCustomUserColorsToUserAsync(Guid sourceToken, ExportCustomUserColorsToUserRequest request, CancellationToken ct = default) => Throw(nameof(ExportCustomUserColorsToUserAsync), ct);
    public Task UpdateCustomUserColorAsync(Guid token, UpdateCustomUserColorRequest request, CancellationToken ct = default) => Throw(nameof(UpdateCustomUserColorAsync), ct);
    public Task AddPasswordTagAsync(Guid token, NewPasswordTagRequest request, CancellationToken ct = default) => Throw(nameof(AddPasswordTagAsync), ct);
    public Task DeletePasswordTagAsync(Guid token, Guid passwordTagId, CancellationToken ct = default) => Throw(nameof(DeletePasswordTagAsync), ct);
    public Task ExportPasswordTagsToUserAsync(Guid sourceToken, ExportPasswordTagsToUserRequest request, CancellationToken ct = default) => Throw(nameof(ExportPasswordTagsToUserAsync), ct);
    public Task UpdatePasswordTagAsync(Guid token, UpdatePasswordTagRequest request, CancellationToken ct = default) => Throw(nameof(UpdatePasswordTagAsync), ct);
}
