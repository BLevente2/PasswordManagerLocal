using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserSnapshotInboxService
{
    Task<UserSnapshotReceiptResult> StoreAsync(UserSnapshotEnvelope envelope, Guid transportPeerDeviceId, CancellationToken ct = default);
}
