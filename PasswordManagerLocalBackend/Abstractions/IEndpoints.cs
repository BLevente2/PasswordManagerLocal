using PasswordManagerLocalBackend.Requests;

namespace PasswordManagerLocalBackend.Abstractions;

public interface IEndpoints
{
    Task<string> RegisterAsync(RegistrationRequest request, CancellationToken ct = default);
    Task<string> LoginAsync(LoginRequest request, CancellationToken ct = default);


    Task<IReadOnlyList<string>> InicializeAllRememberMeAsync(CancellationToken ct = default);
    Task SetRememberMeAsync(string token, bool rememberMe, CancellationToken ct = default);
}