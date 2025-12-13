using PasswordManagerLocalBackend.Abstractions;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend;

public sealed class Endpoints : IEndpoints
{
    private readonly IAuthService _authService;
    private readonly IRememberMeService _rememberMeService;
    private readonly IUserPasswordsService _userPasswordsService;

    public Endpoints
        (
        IAuthService authService,
        IRememberMeService rememberMeService,
        IUserPasswordsService userPasswordsService
        )
    {
        _authService = authService;
        _rememberMeService = rememberMeService;
        _userPasswordsService = userPasswordsService;
    }





    public Task<string> RegisterAsync(RegistrationRequest request, CancellationToken ct = default) =>
        _authService.RegisterAsync(request, ct);


    public Task<string> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        _authService.LoginAsync(request, ct);




    public Task<IReadOnlyList<string>> InicializeAllRememberMeAsync(CancellationToken ct = default) =>
        _rememberMeService.InicializeAllRememberMeAsync(ct);


    public Task SetRememberMeAsync(string token, bool rememberMe, CancellationToken ct = default) =>
        _rememberMeService.SetRememberMeAsync(token, rememberMe, ct);




    public Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(string token, CancellationToken ct = default) =>
        _userPasswordsService.GetSavedPasswordsAsync(token, ct);
}