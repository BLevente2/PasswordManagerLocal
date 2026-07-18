using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Models;
using System.Collections.Concurrent;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> _store = new();
    private int _listAllCallCount;
    private int _loginLookupCallCount;

    public int ListAllCallCount => Volatile.Read(ref _listAllCallCount);
    public int LoginLookupCallCount => Volatile.Read(ref _loginLookupCallCount);

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

    public Task<IReadOnlyList<UserLoginLookupData>> ListLoginLookupDataAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _loginLookupCallCount);

        var list = _store.Values
            .Select(user => new UserLoginLookupData
            {
                UId = user.UId,
                UsernameSalt = user.UsernameSalt.ToArray(),
                UsernameHash = user.UsernameHash.ToArray()
            })
            .ToList();

        return Task.FromResult((IReadOnlyList<UserLoginLookupData>)list);
    }


    public Task<IReadOnlyList<User>> GetAllRememberMeEnabledUsersAsync(CancellationToken ct = default)
    {
        var list = _store.Values
            .Where(u => u.SavedKey is not null && u.SavedKey.Length > 0)
            .Select(Clone)
            .ToList();

        return Task.FromResult((IReadOnlyList<User>)list);
    }

    public Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var users = ids.Where(_store.ContainsKey).Select(id => Clone(_store[id])).ToList();
        return Task.FromResult((IReadOnlyList<User>)users);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var u))
            return Task.FromResult<User?>(Clone(u));

        return Task.FromResult<User?>(null);
    }

    public Task<User?> GetByIdAsNoTrackingAsync(Guid id, CancellationToken ct = default)
        => GetByIdAsync(id, ct);

    public Task<User?> GetByIdWithRelationsAsync(Guid id, CancellationToken ct = default)
        => GetByIdAsync(id, ct);

    public Task<User?> GetByIdAsNoTrackingWithRelationsAsync(Guid id, CancellationToken ct = default)
        => GetByIdAsync(id, ct);


    public Task UpdateSavedKeyAsync(Guid id, byte[]? savedKey, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var user))
        {
            var replacement = Clone(user);
            replacement.SavedKey = savedKey?.ToArray();
            _store[id] = replacement;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<User>> ListAllAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _listAllCallCount);
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
            EncryptedGeneralUserDataPayload = u.EncryptedGeneralUserDataPayload.ToArray(),
            EncryptedUserPasswordsDataPayload = u.EncryptedUserPasswordsDataPayload.ToArray(),
            EncryptedUserDevicesDataPayload = u.EncryptedUserDevicesDataPayload.ToArray(),
            SavedKey = u.SavedKey is null ? null : u.SavedKey.ToArray(),
            KeyEpoch = u.KeyEpoch,
            MembershipEpoch = u.MembershipEpoch,
            LastModifiedAt = u.LastModifiedAt,
            UserDataLastModifiedAt = u.UserDataLastModifiedAt,
            GeneralUserDataLastModifiedAt = u.GeneralUserDataLastModifiedAt,
            UserPasswordsDataLastModifiedAt = u.UserPasswordsDataLastModifiedAt,
            UserDevicesDataLastModifiedAt = u.UserDevicesDataLastModifiedAt,
            IntegrityHash = u.IntegrityHash.ToArray(),
            Groups = u.Groups.ToList(),
            LocalUserDevices = u.LocalUserDevices.ToList(),
            UserDevices = u.UserDevices.ToList()
        };
    }
}
