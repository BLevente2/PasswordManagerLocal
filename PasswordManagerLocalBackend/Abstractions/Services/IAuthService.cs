using PasswordManagerLocalBackend.DTOs;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IAuthService
{
    Task<string> RegisterAsync(RegistrationDTO dto, CancellationToken ct = default);
    Task<string> LoginAsync(LoginDTO dto, CancellationToken ct = default);
}