using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserLoginService
{
    Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default);
}
