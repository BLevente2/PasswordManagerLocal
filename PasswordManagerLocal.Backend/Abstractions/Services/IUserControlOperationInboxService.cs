using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserControlOperationInboxService
{
    Task<UserControlOperationReceiptResult> StoreAndApplyAsync(
        UserControlOperationEnvelope envelope,
        Guid transportPeerDeviceId,
        CancellationToken ct = default);

    Task<UserControlOperationReceiptResult> TryApplyStoredAsync(
        Guid operationId,
        CancellationToken ct = default);
}
