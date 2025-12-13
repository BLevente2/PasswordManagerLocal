using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions;

public interface IEndpoints
{
    Task<string> RegisterAsync(RegistrationRequest request, CancellationToken ct = default);
    Task<string> LoginAsync(LoginRequest request, CancellationToken ct = default);


    Task<IReadOnlyList<string>> InicializeAllRememberMeAsync(CancellationToken ct = default);
    Task SetRememberMeAsync(string token, bool rememberMe, CancellationToken ct = default);


    Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(string token, CancellationToken ct = default);
}