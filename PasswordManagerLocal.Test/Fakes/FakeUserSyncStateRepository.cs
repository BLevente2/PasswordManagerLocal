using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserSyncStateRepository : IUserSyncStateRepository
{
    private readonly Dictionary<Guid, UserSyncState> _rows = [];

    public Task<UserSyncState?> GetAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_rows.GetValueOrDefault(userId));

    public Task AddAsync(UserSyncState state, CancellationToken ct = default)
    {
        _rows.Add(state.UserId, state);
        return Task.CompletedTask;
    }

    public void Update(UserSyncState state) => _rows[state.UserId] = state;
}
