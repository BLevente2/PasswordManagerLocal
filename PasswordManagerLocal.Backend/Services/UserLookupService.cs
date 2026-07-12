using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Loads and integrity-verifies user records without decrypting the encrypted user-data aggregate.
/// </summary>
public sealed class UserLookupService : IUserLookupService
{
    private readonly IUserRepository _users;
    private readonly IUserSessionService _sessions;

    public UserLookupService(IUserRepository users, IUserSessionService sessions)
    {
        _users = users;
        _sessions = sessions;
    }

    public Task<User?> GetUserByUidAsync(Guid uid, CancellationToken ct = default) =>
        _users.GetByIdAsync(uid, ct);

    public async Task<User> GetAndVerifyUserByUidAsync(Guid uid, CancellationToken ct = default)
    {
        var user = await GetUserByUidAsync(uid, ct);
        if (user is null)
            throw new UserNotFoundException();

        user.VerifyIntegrity();
        return user;
    }

    public Task<User> GetAndVerifyUserAsync(Guid token, CancellationToken ct = default) =>
        GetAndVerifyUserByUidAsync(_sessions.GetUidFromToken(token), ct);

    public async Task<User?> GetUserByUsernameAsync(byte[] username, CancellationToken ct = default)
    {
        var lookupData = await _users.ListLoginLookupDataAsync(ct);

        foreach (var candidate in lookupData)
        {
            var calculatedHash = Hashing.SHA256Hash(username, candidate.UsernameSalt);
            try
            {
                if (!Hashing.Verify(candidate.UsernameHash, calculatedHash))
                    continue;

                return await _users.GetByIdAsync(candidate.UId, ct);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(calculatedHash);
            }
        }

        return null;
    }

    public async Task<User> GetAndVerifyUserByUsernameAsync(byte[] username, CancellationToken ct = default)
    {
        var user = await GetUserByUsernameAsync(username, ct);
        if (user is null)
            throw new UserNotFoundException();

        user.VerifyIntegrity();
        return user;
    }

    public async Task<IReadOnlyList<User>> GetAndVerifyRememberMeEnabledUsersAsync(CancellationToken ct = default)
    {
        var users = await _users.GetAllRememberMeEnabledUsersAsync(ct);
        var verifiedUsers = new List<User>(users.Count);

        foreach (var user in users)
        {
            user.VerifyIntegrity();
            verifiedUsers.Add(user);
        }

        return verifiedUsers;
    }

    public async Task<bool> UserExistsAsync(Guid uid, CancellationToken ct = default)
    {
        var user = await GetUserByUidAsync(uid, ct);
        return user is not null;
    }
}
