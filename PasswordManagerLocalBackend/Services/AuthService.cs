using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.DTOs;

namespace PasswordManagerLocalBackend.Services;

public sealed class AuthService : IAuthService
{
    public Task<byte[]> RegisterAsync(RegistrationDTO dto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public Task<byte[]> LoginAsync(LoginDTO dto, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}