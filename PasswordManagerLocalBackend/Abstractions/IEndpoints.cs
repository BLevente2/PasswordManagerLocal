using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions;

public interface IEndpoints
{
    Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default);
    Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default);
    void Logout(Guid token);
    Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default);


    Task<UserProfileInfoResponse> GetUserProfileInfoAsync(Guid token, CancellationToken ct = default);
    Task DeleteUserAccountAsync(Guid token, byte[] password, CancellationToken ct = default);
    Task ChangeUsernameAsync(Guid token, string newUsername, CancellationToken ct = default);
    Task UpdateUserProfileInfoAsync(UpdateUserProfileRequest request, CancellationToken ct = default);


    Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default);
    Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default);


    Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default);
    Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default);
    Task RemovePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default);
    Task<byte[]> GetUnsecurePasswordAsync(Guid token, Guid passwordId, CancellationToken ct = default);
    Task UpdatePasswordAsync(Guid token, UpdatePasswordRequest request, CancellationToken ct = default);
}