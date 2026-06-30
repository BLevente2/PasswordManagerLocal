using PasswordManagerLocalBackend.Requests;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IUserCustomColorService
{
    Task AddCustomUserColorAsync(Guid token, NewCustomUserColorRequest request, CancellationToken ct = default);
    Task DeleteCustomUserColorAsync(Guid token, Guid customUserColorId, CancellationToken ct = default);
    Task UpdateCustomUserColorAsync(Guid token, UpdateCustomUserColorRequest request, CancellationToken ct = default);
}
