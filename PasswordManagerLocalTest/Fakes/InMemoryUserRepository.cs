using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Models;
using System.Collections.Concurrent;

namespace PasswordManagerLocalTest.Fakes;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> _store = new();

    public Task AddAsync(User entity, CancellationToken ct = default)
    {
        _store[entity.UId] = Clone(entity);
        return Task.CompletedTask;
    }

    public void Delete(User entity)
    {
        _store.TryRemove(entity.UId, out _);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_store.ContainsKey(id));

    public Task<IReadOnlyList<User>> GetAllRememberMeEnabledUsersAsync(CancellationToken ct = default)
    {
        var list = _store.Values
            .Where(u => u.SavedKey is not null && u.SavedKey.Length > 0)
            .Select(Clone)
            .ToList();

        return Task.FromResult((IReadOnlyList<User>)list);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var u))
            return Task.FromResult<User?>(Clone(u));

        return Task.FromResult<User?>(null);
    }

    public Task<User?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default)
        => GetByIdAsync(id, ct);

    public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct = default)
    {
        var list = _store.Values.Select(Clone).ToList();
        return Task.FromResult((IReadOnlyList<User>)list);
    }

    public Task<(bool found, User? entity)> TryGetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var u))
            return Task.FromResult((true, (User?)Clone(u)));

        return Task.FromResult((false, (User?)null));
    }

    public void Update(User entity)
    {
        _store[entity.UId] = Clone(entity);
    }

    private static User Clone(User u)
    {
        return new User
        {
            UId = u.UId,
            UsernameHash = u.UsernameHash.ToArray(),
            UsernameSalt = u.UsernameSalt.ToArray(),
            PasswordSalt = u.PasswordSalt.ToArray(),
            EncryptedPayload = u.EncryptedPayload.ToArray(),
            SavedKey = u.SavedKey is null ? null : u.SavedKey.ToArray(),
            IntegrityHash = u.IntegrityHash.ToArray(),
            Groups = u.Groups,
            Devices = u.Devices
        };
    }
}