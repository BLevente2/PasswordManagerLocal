using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;

namespace PasswordManagerLocal.Backend.Repositories;

public sealed class UserSyncStateRepository : IUserSyncStateRepository
{
    private readonly DbSet<UserSyncState> _states;

    public UserSyncStateRepository(AppDbContext context)
    {
        _states = context.UserSyncStates;
    }

    public Task<UserSyncState?> GetAsync(Guid userId, CancellationToken ct = default) =>
        _states.FirstOrDefaultAsync(state => state.UserId == userId, ct);

    public Task AddAsync(UserSyncState state, CancellationToken ct = default) =>
        _states.AddAsync(state, ct).AsTask();

    public void Update(UserSyncState state) => _states.Update(state);
}
