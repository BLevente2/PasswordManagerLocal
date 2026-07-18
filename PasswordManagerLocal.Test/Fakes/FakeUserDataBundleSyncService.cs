using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeUserDataBundleSyncService : IUserDataBundleSyncService
{
    public Func<User, IReadOnlyList<UserSnapshotEnvelope>, EncryptionKey, CancellationToken, Task<UserSnapshotMergeBatchResult>>? Handler { get; set; }

    public Task<UserSnapshotMergeBatchResult> TryVerifyAndMergeManyAsync(
        User existing,
        IReadOnlyList<UserSnapshotEnvelope> snapshots,
        EncryptionKey key,
        CancellationToken ct = default) =>
        Handler?.Invoke(existing, snapshots, key, ct)
        ?? Task.FromResult(new UserSnapshotMergeBatchResult(
            CanonicalChanged: false,
            snapshots.Select(snapshot => new UserSnapshotMergeEntryResult(
                snapshot.OriginDeviceId,
                snapshot.OriginInstanceId,
                snapshot.OriginRevision,
                Verified: true)).ToArray()));
}
