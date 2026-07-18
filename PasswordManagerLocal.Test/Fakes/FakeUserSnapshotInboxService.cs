using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserSnapshotInboxService : IUserSnapshotInboxService
{
    public UserSnapshotReceiptResult? Result { get; set; }

    public Task<UserSnapshotReceiptResult> StoreAsync(
        UserSnapshotEnvelope envelope,
        Guid transportPeerDeviceId,
        CancellationToken ct = default) =>
        Task.FromResult(Result ?? new UserSnapshotReceiptResult(
            envelope.UserId,
            envelope.OriginDeviceId,
            envelope.OriginInstanceId,
            envelope.OriginRevision,
            envelope.SnapshotHash.ToArray(),
            UserSnapshotReceiptState.StoredPending));
}
