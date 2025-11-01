using PasswordManagerLocalBackend.DTOs;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IAuthService
{
    Task<byte[]> RegisterAsync(RegistrationDTO dto, CancellationToken ct = default);
    Task<byte[]> LoginAsync(LoginDTO dto, CancellationToken ct = default);
}