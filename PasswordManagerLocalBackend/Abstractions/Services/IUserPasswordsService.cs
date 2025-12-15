using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserPasswordsService
{
    Task<IReadOnlyList<PasswordInfoResponse>> GetSavedPasswordsAsync(Guid token, CancellationToken ct = default);
    Task AddNewPasswordAsync(Guid token, NewPasswordRequest request, CancellationToken ct = default);
}