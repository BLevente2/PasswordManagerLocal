using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserPasswordsService
{
    Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(string token, CancellationToken ct = default);
}