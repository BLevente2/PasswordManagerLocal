using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions;

public interface IEndpoints
{
    Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default);
    Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default);
    void Logout(Guid token);


    Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default);
    Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default);


    Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default);
    Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default);
}