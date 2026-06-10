using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Responses;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IAuthService
{
    Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default);
    Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default);
    void Logout(Guid token);
    void LogoutUser(Guid uid);
    void LogoutUser(Guid uid, AuthSessionInvalidationReason reason);
    AuthSessionStatusResponse GetSessionStatus(Guid token);
    Task RefreshSyncedUserSessionsAsync(User user, CancellationToken ct = default);
    Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default);
    bool IsPasswordValid(Guid token, byte[] password, byte[] salt);
}