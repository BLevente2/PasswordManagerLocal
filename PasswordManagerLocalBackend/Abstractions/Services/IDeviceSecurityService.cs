using PasswordManagerLocalBackend.Models;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface IDeviceSecurityService
{
    Task RecordInvalidIncomingSyncAsync(Device device, string reason, CancellationToken ct = default);
    Task ResetInvalidIncomingSyncAsync(Device device, CancellationToken ct = default);
}
