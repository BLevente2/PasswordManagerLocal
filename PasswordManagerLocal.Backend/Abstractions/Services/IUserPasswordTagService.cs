using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserPasswordTagService
{
    Task AddPasswordTagAsync(Guid token, NewPasswordTagRequest request, CancellationToken ct = default);
    Task DeletePasswordTagAsync(Guid token, Guid passwordTagId, CancellationToken ct = default);
    Task UpdatePasswordTagAsync(Guid token, UpdatePasswordTagRequest request, CancellationToken ct = default);
}
