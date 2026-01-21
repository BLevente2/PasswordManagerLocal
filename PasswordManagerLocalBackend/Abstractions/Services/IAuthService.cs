using PasswordManagerLocalBackend.Requests;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IAuthService
{
    Task<Guid> RegisterAsync(RegistrationRequest request, CancellationToken ct = default);
    Task<Guid> LoginAsync(LoginRequest request, CancellationToken ct = default);
    void Logout(Guid token);
    Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default);
    bool IsPasswordValid(Guid token, byte[] password, byte[] salt);
}