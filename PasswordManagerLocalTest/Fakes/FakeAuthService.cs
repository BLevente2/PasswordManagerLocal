using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeAuthService : IAuthService
{
    public List<(Guid UserId, AuthSessionInvalidationReason Reason)> LogoutUserCalls { get; } = [];
    public List<User> RefreshedUsers { get; } = [];

    public Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Guid> RenewSessionAsync(Guid token, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public void Logout(Guid token) => throw new NotSupportedException();

    public void LogoutUser(Guid uid) =>
        LogoutUser(uid, AuthSessionInvalidationReason.LoggedOut);

    public void LogoutUser(Guid uid, AuthSessionInvalidationReason reason) =>
        LogoutUserCalls.Add((uid, reason));

    public AuthSessionStatusResponse GetSessionStatus(Guid token) =>
        throw new NotSupportedException();

    public Task RefreshSyncedUserSessionsAsync(User user, CancellationToken ct = default)
    {
        RefreshedUsers.Add(user);
        return Task.CompletedTask;
    }

    public Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public bool IsPasswordValid(Guid token, byte[] password, byte[] salt) => false;
}
