using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserSnapshotMergeCoordinator : IUserSnapshotMergeCoordinator
{
    private readonly IUserRepository _users;
    private readonly IUserMembershipAuthorizationService _membershipAuthorization;
    private readonly IUserSyncSnapshotRepository _snapshots;
    private readonly IUserRevisionKnowledgeRepository _knowledge;
    private readonly IUserDataBundleSyncService _bundleSync;
    private readonly IUserSnapshotPublisherService _publisher;
    private readonly ISyncQueueWriterService _queueWriter;
    private readonly IPendingSyncActivationService _activation;
    private readonly IUnitOfWork _uow;
    private readonly IUserLifecycleCoordinator _lifecycle;
    private readonly IDeletedUserBarrierRepository? _deletionBarriers;

    public UserSnapshotMergeCoordinator(
        IUserRepository users,
        IUserMembershipAuthorizationService membershipAuthorization,
        IUserSyncSnapshotRepository snapshots,
        IUserRevisionKnowledgeRepository knowledge,
        IUserDataBundleSyncService bundleSync,
        IUserSnapshotPublisherService publisher,
        ISyncQueueWriterService queueWriter,
        IPendingSyncActivationService activation,
        IUnitOfWork uow,
        IUserLifecycleCoordinator lifecycle,
        IDeletedUserBarrierRepository? deletionBarriers = null)
    {
        _users = users;
        _membershipAuthorization = membershipAuthorization;
        _snapshots = snapshots;
        _knowledge = knowledge;
        _bundleSync = bundleSync;
        _publisher = publisher;
        _queueWriter = queueWriter;
        _activation = activation;
        _uow = uow;
        _lifecycle = lifecycle;
        _deletionBarriers = deletionBarriers;
    }

    public Task<bool> TryMergePendingAsync(Guid userId, EncryptionKey key, CancellationToken ct = default) =>
        _lifecycle.ExecuteAsync(userId, token => TryMergePendingCoreAsync(userId, key, token), ct);

    public Task<bool> TryMergePendingUnderLifecycleAsync(Guid userId, EncryptionKey key, CancellationToken ct = default) =>
        TryMergePendingCoreAsync(userId, key, ct);

    private async Task<bool> TryMergePendingCoreAsync(Guid userId, EncryptionKey key, CancellationToken ct)
    {
        if (_deletionBarriers is not null && await _deletionBarriers.ExistsAsync(userId, ct))
            return false;

        await using var transaction = await _uow.BeginTransactionAsync(ct);
        if (_deletionBarriers is not null && await _deletionBarriers.ExistsAsync(userId, ct))
        {
            await transaction.RollbackAsync(ct);
            return false;
        }
        var user = await _users.GetByIdWithRelationsAsync(userId, ct);
        if (user is null)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var captured = await _snapshots.ListPendingForKeyEpochAsync(userId, user.KeyEpoch, ct);
        if (captured.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var candidates = new List<(UserSyncSnapshot Row, UserSnapshotEnvelope Envelope)>();
        foreach (var row in captured
                     .OrderBy(snapshot => snapshot.OriginDeviceId)
                     .ThenBy(snapshot => snapshot.OriginInstanceId)
                     .ThenBy(snapshot => snapshot.OriginRevision))
        {
            try
            {
                var envelope = Deserialize(row);
                await _membershipAuthorization.VerifySnapshotAuthorAsync(envelope, ct);
                if (envelope.UserKeyEpoch != user.KeyEpoch || envelope.MembershipEpoch > user.MembershipEpoch)
                    throw new InvalidDataException("The snapshot epoch is not safely applicable to canonical state.");

                candidates.Add((row, envelope));
            }
            catch (Exception ex) when (IsCandidateFailure(ex))
            {
                Quarantine(row, ex.Message);
            }
        }

        if (candidates.Count == 0)
        {
            await _uow.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return false;
        }

        UserSnapshotMergeBatchResult mergeResult;
        try
        {
            mergeResult = await _bundleSync.TryVerifyAndMergeManyAsync(
                user,
                candidates.Select(candidate => candidate.Envelope).ToArray(),
                key,
                ct);
        }
        catch (DeterministicSyncConflictException)
        {
            // Exact-version/content disagreement is an impossible state under valid local
            // generation. Roll back all canonical work and surface the non-secret conflict
            // diagnostics instead of silently resolving or quarantining by arrival order.
            await transaction.RollbackAsync(CancellationToken.None);
            _uow.ClearTrackedChanges();
            throw;
        }
        catch (Exception ex) when (IsCandidateFailure(ex))
        {
            // A decryption/integrity failure may occur after an in-memory candidate has begun
            // merging. Roll the whole unit back so no partial canonical metadata can be
            // persisted; the immutable candidates remain pending.
            await transaction.RollbackAsync(CancellationToken.None);
            _uow.ClearTrackedChanges();
            return false;
        }

        var results = mergeResult.Entries.ToDictionary(
            result => (result.OriginDeviceId, result.OriginInstanceId, result.OriginRevision));
        var mergedRows = new List<UserSyncSnapshot>();
        foreach (var candidate in candidates)
        {
            var keyTuple = (
                candidate.Envelope.OriginDeviceId,
                candidate.Envelope.OriginInstanceId,
                candidate.Envelope.OriginRevision);
            if (!results.TryGetValue(keyTuple, out var result) || !result.Verified)
            {
                Quarantine(
                    candidate.Row,
                    result?.FailureReason ?? "The encrypted snapshot could not be decrypted and verified with the active user key.");
                continue;
            }

            await RecordMergedKnowledgeAsync(candidate.Envelope, ct);
            mergedRows.Add(candidate.Row);
        }

        if (mergedRows.Count == 0)
        {
            await _uow.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return false;
        }

        // Persist the canonical merge and newly created revision-knowledge rows inside the
        // still-open transaction before the publisher queries coverage. EF queries do not
        // include Added rows that have not yet been saved, which previously caused the fresh
        // local snapshot to omit the exact remote revisions it had just merged.
        await _uow.SaveChangesAsync(ct);

        if (_deletionBarriers is not null && await _deletionBarriers.ExistsAsync(userId, ct))
        {
            await transaction.RollbackAsync(ct);
            _uow.ClearTrackedChanges();
            return false;
        }

        var localSnapshot = await _publisher.GetOrCreateAsync(user, ct);

        await _queueWriter.EnqueueAsync(
            new SyncItem
            {
                ModelId = user.UId,
                ModelType = SyncModelType.User,
                ChangeType = SyncChangeType.Updated,
                ChangedAtTs = localSnapshot.CreatedAtUtc.ToUnixTimeMilliseconds()
            },
            localSnapshot.CreatedAtUtc.ToUnixTimeMilliseconds(),
            [],
            touchLocalSyncState: true,
            activateTargets: false,
            ct);

        _snapshots.DeleteRange(mergedRows);
        if (_deletionBarriers is not null && await _deletionBarriers.ExistsAsync(userId, ct))
        {
            await transaction.RollbackAsync(ct);
            _uow.ClearTrackedChanges();
            return false;
        }
        await _uow.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        try
        {
            // The queue row is durable. Activation is only a wake-up optimization and must not
            // turn a committed merge into a reported merge failure.
            await _activation.ActivatePendingAsync(CancellationToken.None);
        }
        catch
        {
        }
        return true;
    }


    private async Task RecordMergedKnowledgeAsync(UserSnapshotEnvelope envelope, CancellationToken ct)
    {
        await UpsertMergedAsync(
            envelope.UserId,
            envelope.OriginDeviceId,
            envelope.OriginInstanceId,
            envelope.UserKeyEpoch,
            envelope.OriginRevision,
            envelope.SnapshotHash,
            ct);

        foreach (var covered in envelope.Coverage
                     .Where(item => item.UserKeyEpoch == envelope.UserKeyEpoch && item.OriginRevision > 0)
                     .OrderBy(item => item.OriginDeviceId)
                     .ThenBy(item => item.OriginInstanceId))
        {
            await UpsertMergedAsync(
                envelope.UserId,
                covered.OriginDeviceId,
                covered.OriginInstanceId,
                covered.UserKeyEpoch,
                covered.OriginRevision,
                [],
                ct);
        }
    }

    private async Task UpsertMergedAsync(
        Guid userId,
        Guid originDeviceId,
        Guid originInstanceId,
        long keyEpoch,
        long revision,
        byte[] snapshotHash,
        CancellationToken ct)
    {
        var item = await _knowledge.GetAsync(userId, originDeviceId, originInstanceId, keyEpoch, ct);
        var isNew = item is null;
        item ??= new UserRevisionKnowledge
        {
            UserId = userId,
            OriginDeviceId = originDeviceId,
            OriginInstanceId = originInstanceId,
            UserKeyEpoch = keyEpoch
        };

        // Stored knowledge is advanced only when this exact immutable envelope and hash were
        // durably present. Coverage can advance merged knowledge without claiming that the
        // covered envelope itself is retained locally.
        if (snapshotHash.Length == Constants.SyncConstants.SyncDeltaPayloadHashBytes &&
            revision > item.HighestStoredRevision)
        {
            item.HighestStoredRevision = revision;
            item.HighestStoredSnapshotHash = snapshotHash.ToArray();
        }
        item.HighestMergedRevision = Math.Max(item.HighestMergedRevision, revision);
        item.LastUpdatedAtUtc = DateTimeOffset.UtcNow;

        if (isNew)
            await _knowledge.AddAsync(item, ct);
        else
            _knowledge.Update(item);
    }

    private void Quarantine(UserSyncSnapshot row, string reason)
    {
        row.Status = UserSyncSnapshotStatus.Quarantined;
        row.QuarantineReason = reason.Length <= 512 ? reason : reason[..512];
        _snapshots.Update(row);
    }

    private static bool IsCandidateFailure(Exception ex) =>
        ex is InvalidDataException or
            UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or
            PasswordManagerLocal.Backend.Exceptions.InvalidDataIntegrityException or
            JsonException;


    private static UserSnapshotEnvelope Deserialize(UserSyncSnapshot row)
    {
        if (row.EnvelopePayload.Length == 0 || row.EnvelopePayload.Length > Constants.SyncConstants.MaxUserSnapshotEnvelopeBytes)
            throw new InvalidDataException("The stored user snapshot envelope size is invalid.");

        return JsonSerializer.Deserialize(
                   row.EnvelopePayload,
                   BackendJsonSerializerContext.Default.UserSnapshotEnvelope)
               ?? throw new InvalidDataException("The stored user snapshot envelope is invalid.");
    }
}
