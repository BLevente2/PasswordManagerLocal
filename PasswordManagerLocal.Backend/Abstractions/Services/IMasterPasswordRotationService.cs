using PasswordManagerLocal.Backend.Requests;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IMasterPasswordRotationService
{
    Task ChangeMasterPasswordAsync(MasterPasswordChangeRequest request, CancellationToken ct = default);
}
