using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserSnapshotMergeCoordinator : IUserSnapshotMergeCoordinator
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UserLocks = new();

    private readonly IUserRepository _users;
    private readonly IDeviceRepository _devices;
    private readonly IUserDeviceRepository _userDevices;
    private readonly IUserSyncSnapshotRepository _snapshots;
    private readonly IUserRevisionKnowledgeRepository _knowledge;
    private readonly IUserDataBundleSyncService _bundleSync;
    private readonly IUserSnapshotPublisherService _publisher;
    private readonly ISyncQueueWriterService _queueWriter;
    private readonly IPendingSyncActivationService _activation;
    private readonly IUnitOfWork _uow;

    public UserSnapshotMergeCoordinator(
        IUserRepository users,
        IDeviceRepository devices,
        IUserDeviceRepository userDevices,
        IUserSyncSnapshotRepository snapshots,
        IUserRevisionKnowledgeRepository knowledge,
        IUserDataBundleSyncService bundleSync,
        IUserSnapshotPublisherService publisher,
        ISyncQueueWriterService queueWriter,
        IPendingSyncActivationService activation,
        IUnitOfWork uow)
    {
        _users = users;
        _devices = devices;
        _userDevices = userDevices;
        _snapshots = snapshots;
        _knowledge = knowledge;
        _bundleSync = bundleSync;
        _publisher = publisher;
        _queueWriter = queueWriter;
        _activation = activation;
        _uow = uow;
    }

    public async Task<bool> TryMergePendingAsync(Guid userId, EncryptionKey key, CancellationToken ct = default)
    {
        var gate = UserLocks.GetOrAdd(userId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var transaction = await _uow.BeginTransactionAsync(ct);
            var user = await _users.GetByIdWithRelationsAsync(userId, ct);
            if (user is null)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            var captured = await _snapshots.ListPendingAsync(userId, user.KeyEpoch, user.MembershipEpoch, ct);
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
                    var originDevice = await _devices.GetByIdAsync(envelope.OriginDeviceId, ct)
                        ?? throw new UnauthorizedAccessException("The snapshot origin device is no longer trusted.");
                    UserSnapshotEnvelopeUtil.Verify(envelope, originDevice);
                    if (!await _userDevices.HasActiveLinkAsync(envelope.UserId, envelope.OriginDeviceId, ct))
                        throw new UnauthorizedAccessException("The snapshot origin device is no longer authorized for this user.");

                    if (envelope.UserKeyEpoch != user.KeyEpoch || envelope.MembershipEpoch != user.MembershipEpoch)
                        throw new InvalidDataException("The snapshot epoch is no longer compatible with canonical state.");

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
            catch (Exception ex) when (IsCandidateFailure(ex))
            {
                // The active key or canonical state could not be opened. Keep every valid candidate pending;
                // pre-validation quarantines are still durable and no canonical state is changed.
                await _uow.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
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
            await _uow.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await _activation.ActivatePendingAsync(ct);
            return true;
        }
        finally
        {
            gate.Release();
        }
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
