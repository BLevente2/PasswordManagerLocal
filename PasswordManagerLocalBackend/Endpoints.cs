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

    public Endpoints
        (
        IAuthService authService,
        IUserProfileService userProfileService,
        IRememberMeService rememberMeService,
        IUserPasswordsService userPasswordsService
        )
    {
        _authService = authService;
        _userProfileService = userProfileService;
        _rememberMeService = rememberMeService;
        _userPasswordsService = userPasswordsService;
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