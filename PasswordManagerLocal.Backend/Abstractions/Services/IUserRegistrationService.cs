using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserRegistrationService
{
    Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default);
}
