using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync.Recovery;

namespace PasswordManagerLocal.Backend.Abstractions.Services;

public interface IUserDataBundleSyncService
{
    Task<UserSnapshotMergeBatchResult> TryVerifyAndMergeManyAsync(
        User existing,
        IReadOnlyList<UserSnapshotEnvelope> snapshots,
        EncryptionKey key,
        CancellationToken ct = default);

    Task<UserSnapshotMergeBatchResult> TryVerifyAndMergeManyAsync(
        User existing,
        IReadOnlyList<UserSnapshotEnvelope> snapshots,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        CancellationToken ct = default) =>
        TryVerifyAndMergeManyAsync(existing, snapshots, key, ct);
    Task<UserDataRecoveryReconstructionResult> TryReconstructCanonicalAsync(
        User existing,
        IReadOnlyList<UserSnapshotEnvelope> snapshots,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Canonical recovery reconstruction is not supported by this implementation.");

    Task<UserDataRecoveryReconstructionResult> TryReconstructCanonicalAsync(
        User existing,
        IReadOnlyList<UserSnapshotEnvelope> snapshots,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        long expectedKeyEpoch,
        long expectedMembershipEpoch,
        CancellationToken ct = default) =>
        TryReconstructCanonicalAsync(existing, snapshots, key, keyConfidence, ct);

}
