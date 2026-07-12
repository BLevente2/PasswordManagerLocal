using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IPendingSyncActivationService
{
    void ActivateDevices(IReadOnlyList<Device> devices);
    Task ActivatePendingAsync(CancellationToken ct = default);
}
