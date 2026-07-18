using Microsoft.EntityFrameworkCore;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Persistence;

namespace PasswordManagerLocal.Backend.Repositories;

public sealed class UserControlStateRepository : IUserControlStateRepository
{
    private readonly DbSet<UserControlState> _states;

    public UserControlStateRepository(AppDbContext context)
    {
        _states = context.UserControlStates;
    }

    public Task<UserControlState?> GetAsync(Guid userId, CancellationToken ct = default) =>
        _states.FirstOrDefaultAsync(state => state.UserId == userId, ct);

    public Task AddAsync(UserControlState state, CancellationToken ct = default) =>
        _states.AddAsync(state, ct).AsTask();

    public void Update(UserControlState state) => _states.Update(state);
}
