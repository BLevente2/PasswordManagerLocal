using PasswordManagerLocalBackend.Requests;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IAuthService
{
    Task<string> RegisterAsync(RegistrationRequest request, CancellationToken ct = default);
    Task<string> LoginAsync(LoginRequest request, CancellationToken ct = default);
}