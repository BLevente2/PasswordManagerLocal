using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserControlStateRepository : IUserControlStateRepository
{
    private readonly Dictionary<Guid, UserControlState> _rows = [];

    public Task<UserControlState?> GetAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_rows.GetValueOrDefault(userId));

    public Task AddAsync(UserControlState state, CancellationToken ct = default)
    {
        _rows.Add(state.UserId, state);
        return Task.CompletedTask;
    }

    public void Update(UserControlState state) => _rows[state.UserId] = state;
}
