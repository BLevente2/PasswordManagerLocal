using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Repositories;

public interface IUserRepository : IGenericRepository<User>
{
    Task<IReadOnlyList<User>> GetAllRememberMeEnabledUsersAsync(CancellationToken ct = default);
}