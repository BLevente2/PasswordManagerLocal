using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Removes encrypted deletion tombstones only after every installation that was authorized when
/// the deletion was created has authenticated true-merge coverage, or has been authoritatively
/// removed with every accepted origin revision incorporated into canonical knowledge.
/// </summary>
public sealed class UserTombstoneGarbageCollector : IUserTombstoneGarbageCollector
{
    private readonly IUserRepository _users;
    private readonly IDeletedUserBarrierRepository _deletionBarriers;
    private readonly IUserMembershipAuthorizationRepository _authorizations;
    private readonly IUserOriginRemovalCutoffRepository _cutoffs;
    private readonly IUserRevisionKnowledgeRepository _knowledge;
    private readonly IUserSyncSnapshotRepository _snapshots;
    private readonly IUserMembershipAuthorizationService _membershipAuthorization;
    private readonly IServiceProvider _services;
    private readonly IUserDataReaderService _reader;
    private readonly IUserDataWriterService _writer;
    private readonly IUserSnapshotPublisherService _publisher;
    private readonly ISyncQueueWriterService _queueWriter;
    private readonly IPendingSyncActivationService _activation;
    private readonly IUserLifecycleCoordinator _lifecycle;
    private readonly ITokenService _tokens;
    private readonly IDataCachingService _cache;
    private readonly IUnitOfWork _uow;

    public UserTombstoneGarbageCollector(
        IUserRepository users,
        IDeletedUserBarrierRepository deletionBarriers,
        IUserMembershipAuthorizationRepository authorizations,
        IUserOriginRemovalCutoffRepository cutoffs,
        IUserRevisionKnowledgeRepository knowledge,
        IUserSyncSnapshotRepository snapshots,
        IUserMembershipAuthorizationService membershipAuthorization,
        IServiceProvider services,
        IUserDataReaderService reader,
        IUserDataWriterService writer,
        IUserSnapshotPublisherService publisher,
        ISyncQueueWriterService queueWriter,
        IPendingSyncActivationService activation,
        IUserLifecycleCoordinator lifecycle,
        ITokenService tokens,
        IDataCachingService cache,
        IUnitOfWork uow)
    {
        _users = users;
        _deletionBarriers = deletionBarriers;
        _authorizations = authorizations;
        _cutoffs = cutoffs;
        _knowledge = knowledge;
        _snapshots = snapshots;
        _membershipAuthorization = membershipAuthorization;
        _services = services;
        _reader = reader;
        _writer = writer;
        _publisher = publisher;
        _queueWriter = queueWriter;
        _activation = activation;
        _lifecycle = lifecycle;
        _tokens = tokens;
        _cache = cache;
        _uow = uow;
    }

    public Task<TombstoneGarbageCollectionResult> CollectAsync(Guid userId, CancellationToken ct = default) =>
        _lifecycle.ExecuteAsync(userId, token => CollectWithResolvedKeyUnderLifecycleAsync(userId, token), ct);

    public Task<TombstoneGarbageCollectionResult> CollectAsync(Guid userId, EncryptionKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _lifecycle.ExecuteAsync(userId, token => CollectUnderLifecycleAsync(userId, key, token), ct);
    }

    private async Task<TombstoneGarbageCollectionResult> CollectWithResolvedKeyUnderLifecycleAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return TombstoneGarbageCollectionResult.Blocked(userId, TombstoneGarbageCollectionReason.UserNotFound);
        if (await _deletionBarriers.ExistsAsync(userId, ct))
            return TombstoneGarbageCollectionResult.Blocked(userId, TombstoneGarbageCollectionReason.AccountDeleted);

        var user = await _users.GetByIdWithRelationsAsync(userId, ct);
        if (user is null)
            return TombstoneGarbageCollectionResult.Blocked(userId, TombstoneGarbageCollectionReason.UserNotFound);
        var keyResolver = _services.GetRequiredService<IUserSyncKeyResolverService>();
        if (!keyResolver.TryResolve(user, out var key) || key is null)
            return TombstoneGarbageCollectionResult.Blocked(userId, TombstoneGarbageCollectionReason.KeyUnavailable);

        using (key)
            return await CollectUnderLifecycleAsync(userId, key, ct);
    }

    private async Task<TombstoneGarbageCollectionResult> CollectUnderLifecycleAsync(
        Guid userId,
        EncryptionKey key,
        CancellationToken ct)
    {
        await using var transaction = await _uow.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            if (await _deletionBarriers.ExistsAsync(userId, ct))
            {
                await transaction.RollbackAsync(ct);
                return TombstoneGarbageCollectionResult.Blocked(userId, TombstoneGarbageCollectionReason.AccountDeleted);
            }

            var user = await _users.GetByIdWithRelationsAsync(userId, ct);
            if (user is null)
            {
                await transaction.RollbackAsync(ct);
                return TombstoneGarbageCollectionResult.Blocked(userId, TombstoneGarbageCollectionReason.UserNotFound);
            }

            var capturedKeyEpoch = user.KeyEpoch;
            var capturedMembershipEpoch = user.MembershipEpoch;
            var retainedSnapshots = await _snapshots.ListForUserAsync(userId, ct);
            if (retainedSnapshots.Count > TombstoneConstants.MaxCausalSnapshotEvidenceRowsPerUser)
                return await RollbackBlockedAsync(transaction, userId, TombstoneGarbageCollectionReason.EvidenceLimitExceeded, ct);
            if (retainedSnapshots.Any(row => row.Status == UserSyncSnapshotStatus.Pending))
                return await RollbackBlockedAsync(transaction, userId, TombstoneGarbageCollectionReason.PendingSnapshotMerge, ct);
            if (retainedSnapshots.Any(row => row.Status == UserSyncSnapshotStatus.Quarantined))
                return await RollbackBlockedAsync(transaction, userId, TombstoneGarbageCollectionReason.QuarantinedEvidence, ct);

            var authorizations = await _authorizations.ListForUserAsync(userId, ct);
            var cutoffs = await _cutoffs.ListForUserAsync(userId, ct);
            var knowledge = await _knowledge.ListForUserAsync(userId, ct);
            if (authorizations.Count > TombstoneConstants.MaxMembershipHistoryRowsPerUser ||
                cutoffs.Count > TombstoneConstants.MaxRemovalCutoffRowsPerUser ||
                knowledge.Count > TombstoneConstants.MaxRevisionKnowledgeRowsPerUser)
            {
                return await RollbackBlockedAsync(transaction, userId, TombstoneGarbageCollectionReason.EvidenceLimitExceeded, ct);
            }
            try
            {
                ValidateEvidenceStructure(
                    userId,
                    capturedKeyEpoch,
                    capturedMembershipEpoch,
                    authorizations,
                    cutoffs,
                    knowledge);
            }
            catch (InvalidDataException)
            {
                return await RollbackBlockedAsync(
                    transaction,
                    userId,
                    TombstoneGarbageCollectionReason.InvalidAuthenticatedEvidence,
                    ct);
            }

            var receiptLoad = await LoadAuthenticatedReceiptsAsync(retainedSnapshots, ct);
            if (receiptLoad.FailureReason is not null)
                return await RollbackBlockedAsync(transaction, userId, receiptLoad.FailureReason.Value, ct);

            using var bundle = await _reader.GetAndVerifyUserDataBundleAsync(user, key, ct);
            var tombstones = EnumerateTombstones(bundle).ToList();
            if (tombstones.Count == 0)
            {
                await transaction.RollbackAsync(ct);
                return new TombstoneGarbageCollectionResult(userId, false, 0, 0, 0, null, []);
            }
            if (tombstones.Count > TombstoneConstants.MaxRetainedUserDataTombstonesPerUser)
                return await RollbackBlockedAsync(transaction, userId, TombstoneGarbageCollectionReason.EvidenceLimitExceeded, ct, tombstones.Count);

            var knowledgeByOrigin = knowledge.ToDictionary(
                row => (row.OriginDeviceId, row.OriginInstanceId, row.UserKeyEpoch));
            var cutoffByAuthorization = cutoffs
                .GroupBy(row => row.AuthorizationId)
                .ToDictionary(group => group.Key, group => group.OrderBy(row => row.UserKeyEpoch).ToArray());

            var stable = new List<TombstoneDescriptor>();
            var diagnostics = new List<TombstoneGarbageCollectionDiagnostic>();
            foreach (var tombstone in tombstones.OrderBy(item => item.ItemType).ThenBy(item => item.ItemId))
            {
                var evaluation = Evaluate(
                    userId,
                    tombstone,
                    authorizations,
                    cutoffByAuthorization,
                    knowledgeByOrigin,
                    receiptLoad.Receipts);
                if (evaluation.Reason == TombstoneGarbageCollectionReason.Stable)
                {
                    stable.Add(tombstone);
                }
                else if (diagnostics.Count < TombstoneConstants.MaxTombstoneGarbageCollectionDiagnostics)
                {
                    diagnostics.Add(new TombstoneGarbageCollectionDiagnostic(
                        userId,
                        tombstone.ItemType,
                        tombstone.ItemId,
                        tombstone.Version,
                        evaluation.Reason,
                        evaluation.BlockingDeviceId,
                        evaluation.BlockingOriginInstanceId,
                        evaluation.BlockingKeyEpoch));
                }
            }

            if (diagnostics.Any(item => item.Reason is
                    TombstoneGarbageCollectionReason.MissingCausalReference or
                    TombstoneGarbageCollectionReason.InvalidCausalReference))
            {
                await transaction.RollbackAsync(ct);
                return TombstoneGarbageCollectionResult.Blocked(
                    userId,
                    TombstoneGarbageCollectionReason.InvalidCausalReference,
                    tombstones.Count,
                    diagnostics);
            }

            if (stable.Count == 0)
            {
                await transaction.RollbackAsync(ct);
                return new TombstoneGarbageCollectionResult(
                    userId,
                    false,
                    tombstones.Count,
                    0,
                    tombstones.Count,
                    null,
                    diagnostics);
            }

            var modifiedBlobs = UserDataBlobKind.None;
            foreach (var descriptor in stable)
                modifiedBlobs |= descriptor.Remove();

            var latest = await _users.GetByIdAsNoTrackingAsync(userId, ct);
            if (latest is null || latest.KeyEpoch != capturedKeyEpoch || latest.MembershipEpoch != capturedMembershipEpoch ||
                await _deletionBarriers.ExistsAsync(userId, ct))
            {
                await transaction.RollbackAsync(ct);
                _uow.ClearTrackedChanges();
                return TombstoneGarbageCollectionResult.Blocked(
                    userId,
                    TombstoneGarbageCollectionReason.ConcurrentCanonicalChange,
                    tombstones.Count,
                    diagnostics);
            }

            SortRemainingTombstones(bundle);
            await _writer.CompactUserDataBundleAsync(bundle, user, key, modifiedBlobs, ct);
            var localSnapshot = await _publisher.GetOrCreateAsync(user, ct);
            await _queueWriter.EnqueueAsync(
                new SyncItem
                {
                    ModelId = userId,
                    ModelType = SyncModelType.User,
                    ChangeType = SyncChangeType.Updated,
                    ChangedAtTs = localSnapshot.CreatedAtUtc.ToUnixTimeMilliseconds()
                },
                localSnapshot.CreatedAtUtc.ToUnixTimeMilliseconds(),
                [],
                touchLocalSyncState: true,
                activateTargets: false,
                ct);

            if (user.KeyEpoch != capturedKeyEpoch || user.MembershipEpoch != capturedMembershipEpoch ||
                await _deletionBarriers.ExistsAsync(userId, ct))
            {
                await transaction.RollbackAsync(ct);
                _uow.ClearTrackedChanges();
                return TombstoneGarbageCollectionResult.Blocked(
                    userId,
                    TombstoneGarbageCollectionReason.ConcurrentCanonicalChange,
                    tombstones.Count,
                    diagnostics);
            }

            await _uow.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            committed = true;

            try
            {
                foreach (var token in _tokens.ListTokensByUid(userId))
                    _cache.InvalidateToken(token);
            }
            catch
            {
                // Canonical compaction and publication are already durable. Cache entries are
                // best-effort accelerators and must not make a committed collection look failed.
            }

            try
            {
                await _activation.ActivatePendingAsync(CancellationToken.None);
            }
            catch
            {
                // The queue and snapshot are already durable; activation is only a wake-up hint.
            }

            return new TombstoneGarbageCollectionResult(
                userId,
                true,
                tombstones.Count,
                stable.Count,
                tombstones.Count - stable.Count,
                null,
                diagnostics);
        }
        catch
        {
            if (!committed)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _uow.ClearTrackedChanges();
            }
            throw;
        }
    }

    private async Task<ReceiptLoadResult> LoadAuthenticatedReceiptsAsync(
        IReadOnlyList<UserSyncSnapshot> rows,
        CancellationToken ct)
    {
        if (rows.Any(row => row.Status is not (
                UserSyncSnapshotStatus.Pending or
                UserSyncSnapshotStatus.LocalPublished or
                UserSyncSnapshotStatus.Quarantined or
                UserSyncSnapshotStatus.MergedReceipt)))
        {
            return new ReceiptLoadResult(
                new Dictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>>(),
                TombstoneGarbageCollectionReason.InvalidAuthenticatedEvidence);
        }

        var receipts = new Dictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>>();
        foreach (var row in rows
                     .Where(row => row.Status is UserSyncSnapshotStatus.LocalPublished or UserSyncSnapshotStatus.MergedReceipt)
                     .OrderBy(row => row.OriginDeviceId)
                     .ThenBy(row => row.OriginInstanceId)
                     .ThenBy(row => row.UserKeyEpoch))
        {
            try
            {
                if (row.EnvelopePayload.Length == 0 || row.EnvelopePayload.Length > SyncConstants.MaxUserSnapshotEnvelopeBytes)
                    throw new InvalidDataException("A retained merged receipt has an invalid envelope size.");
                var envelope = JsonSerializer.Deserialize(
                                   row.EnvelopePayload,
                                   BackendJsonSerializerContext.Default.UserSnapshotEnvelope)
                               ?? throw new InvalidDataException("A retained merged receipt is invalid.");
                await _membershipAuthorization.VerifySnapshotAuthorAsync(envelope, ct);
                if (row.UserId != envelope.UserId ||
                    !Hashing.Verify(row.SnapshotHash, envelope.SnapshotHash) ||
                    !Hashing.Verify(row.OriginSignPublicKey, envelope.OriginSignPublicKey) ||
                    !Hashing.Verify(row.OriginSignature, envelope.OriginSignature) ||
                    row.OriginDeviceId != envelope.OriginDeviceId ||
                    row.OriginInstanceId != envelope.OriginInstanceId ||
                    row.OriginRevision != envelope.OriginRevision ||
                    row.UserKeyEpoch != envelope.UserKeyEpoch ||
                    row.MembershipEpoch != envelope.MembershipEpoch ||
                    row.CreatedAtUtc != envelope.CreatedAtUtc)
                {
                    throw new InvalidDataException("A retained merged receipt row conflicts with its immutable envelope.");
                }

                var key = (envelope.OriginDeviceId, envelope.OriginInstanceId);
                if (!receipts.TryGetValue(key, out var list))
                {
                    list = [];
                    receipts.Add(key, list);
                }
                list.Add(envelope);
            }
            catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                return new ReceiptLoadResult(
                new Dictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>>(),
                TombstoneGarbageCollectionReason.InvalidAuthenticatedEvidence);
            }
        }

        return new ReceiptLoadResult(receipts, null);
    }

    internal static void ValidateEvidenceStructure(
        Guid userId,
        long currentKeyEpoch,
        long currentMembershipEpoch,
        IReadOnlyList<UserMembershipAuthorization> authorizations,
        IReadOnlyList<UserOriginRemovalCutoff> cutoffs,
        IReadOnlyList<UserRevisionKnowledge> knowledge)
    {
        if (userId == Guid.Empty || currentKeyEpoch <= 0 || currentMembershipEpoch <= 0)
            throw new InvalidDataException("The canonical causal-evidence scope is invalid.");

        var authorizationById = new Dictionary<Guid, UserMembershipAuthorization>();
        var exactInstallations = new HashSet<(Guid DeviceId, Guid OriginInstanceId)>();
        var activeDevices = new HashSet<Guid>();
        var membershipTransitionEpochs = new HashSet<long>();
        var genesisCount = 0;
        foreach (var row in authorizations)
        {
            if (row.UserId != userId || row.AuthorizationId == Guid.Empty || row.DeviceId == Guid.Empty ||
                row.OriginInstanceId == Guid.Empty || row.SignPublicKey.Length != SyncConstants.SyncDeltaEd25519PublicKeyBytes ||
                row.SignPublicKeyHash.Length != SyncConstants.SyncDeltaPayloadHashBytes ||
                row.AgreementPublicKeyHash.Length != SyncConstants.SyncDeltaPayloadHashBytes ||
                !Hashing.Verify(row.SignPublicKeyHash, Hashing.SHA256Hash(row.SignPublicKey)) ||
                string.IsNullOrWhiteSpace(row.TlsCertFingerprint) || !DeviceTypeDetector.IsValid(row.DeviceType) ||
                row.StartedMembershipEpoch <= 0 || row.StartedMembershipEpoch > currentMembershipEpoch ||
                row.MinimumKeyEpoch <= 0 || row.MinimumKeyEpoch > currentKeyEpoch ||
                !authorizationById.TryAdd(row.AuthorizationId, row) ||
                !exactInstallations.Add((row.DeviceId, row.OriginInstanceId)))
            {
                throw new InvalidDataException("Membership history contains invalid or conflicting causal evidence.");
            }

            _ = SyncIdentityUtil.NormalizeFingerprint(row.TlsCertFingerprint);

            var hasAdditionId = row.AdditionOperationId is not null;
            var hasAdditionHash = row.AdditionOperationHash is not null;
            if (hasAdditionId != hasAdditionHash ||
                (row.AdditionOperationId is Guid additionId &&
                 (additionId == Guid.Empty || row.AdditionOperationHash!.Length != SyncConstants.SyncDeltaPayloadHashBytes)))
            {
                throw new InvalidDataException("Membership addition evidence is incomplete.");
            }

            if (row.IsGenesis)
            {
                genesisCount++;
                if (row.StartedMembershipEpoch != 1 || hasAdditionId)
                    throw new InvalidDataException("Genesis membership evidence is invalid.");
            }
            else
            {
                if (row.StartedMembershipEpoch <= 1 || !hasAdditionId ||
                    !membershipTransitionEpochs.Add(row.StartedMembershipEpoch))
                {
                    throw new InvalidDataException("Membership addition history is incomplete or conflicting.");
                }
            }

            if (row.IsActive)
            {
                if (!activeDevices.Add(row.DeviceId) || row.EndedMembershipEpoch is not null ||
                    row.MaximumKeyEpoch is not null || row.RemovalOperationId is not null ||
                    row.RemovalOperationHash is not null || row.EndedAtUtc is not null)
                {
                    throw new InvalidDataException("Active membership history contains removal evidence or duplicate device authorization.");
                }
            }
            else
            {
                if (row.EndedMembershipEpoch is not long endedMembershipEpoch ||
                    endedMembershipEpoch <= row.StartedMembershipEpoch || endedMembershipEpoch > currentMembershipEpoch ||
                    row.MaximumKeyEpoch is not long maximumKeyEpoch ||
                    maximumKeyEpoch < row.MinimumKeyEpoch || maximumKeyEpoch > currentKeyEpoch ||
                    row.RemovalOperationId is not Guid removalId || removalId == Guid.Empty ||
                    row.RemovalOperationHash is not { Length: SyncConstants.SyncDeltaPayloadHashBytes } ||
                    row.EndedAtUtc is null ||
                    !membershipTransitionEpochs.Add(endedMembershipEpoch))
                {
                    throw new InvalidDataException("Ended membership history is incomplete or conflicting.");
                }
            }
        }

        if (genesisCount != 1 || activeDevices.Count == 0 ||
            membershipTransitionEpochs.Count != currentMembershipEpoch - 1)
        {
            throw new InvalidDataException("The historical membership transition chain is incomplete.");
        }

        var cutoffNamespaces = new HashSet<(Guid DeviceId, Guid OriginInstanceId, long KeyEpoch)>();
        var cutoffIds = new HashSet<Guid>();
        foreach (var row in cutoffs)
        {
            if (row.UserId != userId || row.CutoffId == Guid.Empty || row.DeviceId == Guid.Empty ||
                row.OriginInstanceId == Guid.Empty || row.UserKeyEpoch <= 0 || row.UserKeyEpoch > currentKeyEpoch ||
                row.HighestAcceptedSnapshotRevision < 0 || row.HighestAcceptedControlSequence < 0 ||
                row.ResultingMembershipEpoch <= 0 || row.ResultingMembershipEpoch > currentMembershipEpoch ||
                row.AuthorizationId == Guid.Empty || row.RemovalOperationId == Guid.Empty ||
                row.RemovalOperationHash.Length != SyncConstants.SyncDeltaPayloadHashBytes ||
                !cutoffIds.Add(row.CutoffId) ||
                !cutoffNamespaces.Add((row.DeviceId, row.OriginInstanceId, row.UserKeyEpoch)) ||
                !authorizationById.TryGetValue(row.AuthorizationId, out var authorization) ||
                authorization.IsActive || authorization.DeviceId != row.DeviceId ||
                authorization.OriginInstanceId != row.OriginInstanceId ||
                authorization.RemovalOperationId != row.RemovalOperationId ||
                authorization.EndedMembershipEpoch != row.ResultingMembershipEpoch ||
                authorization.RemovalOperationHash is null ||
                !Hashing.Verify(authorization.RemovalOperationHash, row.RemovalOperationHash) ||
                row.UserKeyEpoch < authorization.MinimumKeyEpoch ||
                (authorization.MaximumKeyEpoch is long maximum && row.UserKeyEpoch > maximum))
            {
                throw new InvalidDataException("Removal-cutoff history contains invalid or conflicting causal evidence.");
            }
        }

        var cutoffsByAuthorization = cutoffs
            .GroupBy(row => row.AuthorizationId)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.UserKeyEpoch).ToArray());
        foreach (var authorization in authorizations)
        {
            if (authorization.IsActive)
            {
                if (cutoffsByAuthorization.ContainsKey(authorization.AuthorizationId))
                    throw new InvalidDataException("An active membership authorization has removal cutoffs.");
                continue;
            }

            var maximumKeyEpoch = authorization.MaximumKeyEpoch
                ?? throw new InvalidDataException("An ended membership authorization has no maximum key epoch.");
            var expectedCutoffCount = checked(maximumKeyEpoch - authorization.MinimumKeyEpoch + 1);
            if (!cutoffsByAuthorization.TryGetValue(authorization.AuthorizationId, out var authorizationCutoffs) ||
                authorizationCutoffs.LongLength != expectedCutoffCount)
            {
                throw new InvalidDataException("Removal-cutoff history is incomplete for an ended installation.");
            }

            for (var index = 0; index < authorizationCutoffs.Length; index++)
            {
                if (authorizationCutoffs[index].UserKeyEpoch != checked(authorization.MinimumKeyEpoch + index))
                    throw new InvalidDataException("Removal-cutoff key-epoch history contains a gap.");
            }
        }

        var knowledgeNamespaces = new HashSet<(Guid DeviceId, Guid OriginInstanceId, long KeyEpoch)>();
        foreach (var row in knowledge)
        {
            if (row.UserId != userId || row.OriginDeviceId == Guid.Empty || row.OriginInstanceId == Guid.Empty ||
                row.UserKeyEpoch <= 0 || row.UserKeyEpoch > currentKeyEpoch ||
                row.HighestStoredRevision < 0 || row.HighestMergedRevision < 0 ||
                (row.HighestStoredRevision == 0 && row.HighestStoredSnapshotHash.Length != 0) ||
                (row.HighestStoredRevision > 0 && row.HighestStoredSnapshotHash.Length != SyncConstants.SyncDeltaPayloadHashBytes) ||
                !knowledgeNamespaces.Add((row.OriginDeviceId, row.OriginInstanceId, row.UserKeyEpoch)) ||
                !authorizations.Any(authorization =>
                    authorization.DeviceId == row.OriginDeviceId &&
                    authorization.OriginInstanceId == row.OriginInstanceId &&
                    authorization.MinimumKeyEpoch <= row.UserKeyEpoch &&
                    (authorization.MaximumKeyEpoch is null || authorization.MaximumKeyEpoch >= row.UserKeyEpoch)))
            {
                throw new InvalidDataException("Revision knowledge contains invalid or unknown causal evidence.");
            }
        }
    }

    internal static StabilityEvaluation Evaluate(
        Guid userId,
        TombstoneDescriptor tombstone,
        IReadOnlyList<UserMembershipAuthorization> authorizations,
        IReadOnlyDictionary<Guid, UserOriginRemovalCutoff[]> cutoffByAuthorization,
        IReadOnlyDictionary<(Guid DeviceId, Guid OriginInstanceId, long KeyEpoch), UserRevisionKnowledge> knowledge,
        IReadOnlyDictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>> receipts)
    {
        var reference = tombstone.CausalReference;
        if (reference is null || !reference.IsValid)
            return new(TombstoneGarbageCollectionReason.MissingCausalReference);
        if (!tombstone.Version.IsValid ||
            reference.OriginDeviceId != tombstone.Version.OriginDeviceId ||
            reference.OriginInstanceId != tombstone.Version.OriginInstanceId)
        {
            return new(TombstoneGarbageCollectionReason.InvalidCausalReference);
        }

        if (!knowledge.TryGetValue(
                (reference.OriginDeviceId, reference.OriginInstanceId, reference.UserKeyEpoch),
                out var anchorKnowledge) ||
            anchorKnowledge.HighestMergedRevision < reference.OriginRevision)
        {
            return new(
                TombstoneGarbageCollectionReason.StoredOnlyKnowledge,
                reference.OriginDeviceId,
                reference.OriginInstanceId,
                reference.UserKeyEpoch);
        }

        var relevant = authorizations
            .Where(row => row.UserId == userId &&
                          row.StartedMembershipEpoch <= reference.MembershipEpoch &&
                          (row.EndedMembershipEpoch is null || row.EndedMembershipEpoch > reference.MembershipEpoch) &&
                          row.MinimumKeyEpoch <= reference.UserKeyEpoch &&
                          (row.MaximumKeyEpoch is null || row.MaximumKeyEpoch >= reference.UserKeyEpoch))
            .OrderBy(row => row.DeviceId)
            .ThenBy(row => row.OriginInstanceId)
            .ToArray();
        if (relevant.Length == 0 || relevant.All(row =>
                row.DeviceId != reference.OriginDeviceId || row.OriginInstanceId != reference.OriginInstanceId))
        {
            return new(TombstoneGarbageCollectionReason.UnknownHistoricalMembership);
        }

        foreach (var member in relevant)
        {
            if (!member.IsActive && member.EndedMembershipEpoch > reference.MembershipEpoch)
            {
                var removal = EvaluateRemovedMember(member, reference, cutoffByAuthorization, knowledge);
                if (removal.Reason != TombstoneGarbageCollectionReason.Stable)
                    return removal;
                continue;
            }

            if (!receipts.TryGetValue((member.DeviceId, member.OriginInstanceId), out var memberReceipts) ||
                !memberReceipts.Any(envelope => Covers(envelope, reference)))
            {
                return new(
                    TombstoneGarbageCollectionReason.MissingMergedReceipt,
                    member.DeviceId,
                    member.OriginInstanceId,
                    reference.UserKeyEpoch);
            }
        }

        return new(TombstoneGarbageCollectionReason.Stable);
    }

    private static StabilityEvaluation EvaluateRemovedMember(
        UserMembershipAuthorization member,
        TombstoneCausalReference reference,
        IReadOnlyDictionary<Guid, UserOriginRemovalCutoff[]> cutoffByAuthorization,
        IReadOnlyDictionary<(Guid DeviceId, Guid OriginInstanceId, long KeyEpoch), UserRevisionKnowledge> knowledge)
    {
        if (member.RemovalOperationId is null || member.RemovalOperationHash is null ||
            member.EndedMembershipEpoch is null ||
            !cutoffByAuthorization.TryGetValue(member.AuthorizationId, out var cutoffs) ||
            cutoffs.Length == 0 ||
            cutoffs.All(cutoff => cutoff.UserKeyEpoch != reference.UserKeyEpoch))
        {
            return new(
                TombstoneGarbageCollectionReason.MissingRemovalCutoff,
                member.DeviceId,
                member.OriginInstanceId);
        }

        foreach (var cutoff in cutoffs)
        {
            if (cutoff.DeviceId != member.DeviceId ||
                cutoff.OriginInstanceId != member.OriginInstanceId ||
                cutoff.RemovalOperationId != member.RemovalOperationId ||
                cutoff.ResultingMembershipEpoch != member.EndedMembershipEpoch ||
                !Hashing.Verify(cutoff.RemovalOperationHash, member.RemovalOperationHash))
            {
                return new(
                    TombstoneGarbageCollectionReason.InvalidAuthenticatedEvidence,
                    member.DeviceId,
                    member.OriginInstanceId,
                    cutoff.UserKeyEpoch);
            }

            if (cutoff.HighestAcceptedSnapshotRevision == 0)
                continue;

            if (!knowledge.TryGetValue((member.DeviceId, member.OriginInstanceId, cutoff.UserKeyEpoch), out var row) ||
                row.HighestMergedRevision < cutoff.HighestAcceptedSnapshotRevision)
            {
                return new(
                    TombstoneGarbageCollectionReason.RemovalCutoffNotMerged,
                    member.DeviceId,
                    member.OriginInstanceId,
                    cutoff.UserKeyEpoch);
            }
        }

        return new(TombstoneGarbageCollectionReason.Stable);
    }

    internal static bool Covers(UserSnapshotEnvelope envelope, TombstoneCausalReference reference)
    {
        if (envelope.UserKeyEpoch < reference.UserKeyEpoch)
            return false;
        if (envelope.OriginDeviceId == reference.OriginDeviceId &&
            envelope.OriginInstanceId == reference.OriginInstanceId &&
            envelope.UserKeyEpoch == reference.UserKeyEpoch &&
            envelope.OriginRevision >= reference.OriginRevision)
        {
            return true;
        }

        return envelope.Coverage.Any(entry =>
            entry.OriginDeviceId == reference.OriginDeviceId &&
            entry.OriginInstanceId == reference.OriginInstanceId &&
            entry.UserKeyEpoch == reference.UserKeyEpoch &&
            entry.OriginRevision >= reference.OriginRevision);
    }

    internal static void SortRemainingTombstones(UserDataBundle bundle)
    {
        bundle.UserPasswordsData.DeletedPasswords.Sort((left, right) => left.Id.CompareTo(right.Id));
        bundle.UserPasswordsData.DeletedTags.Sort((left, right) => left.Id.CompareTo(right.Id));
        bundle.UserPasswordsData.DeletedCustomColors.Sort((left, right) => left.Id.CompareTo(right.Id));
        bundle.UserDevicesData.DeletedDevices.Sort((left, right) => left.Id.CompareTo(right.Id));
    }

    private static IEnumerable<TombstoneDescriptor> EnumerateTombstones(UserDataBundle bundle)
    {
        foreach (var item in bundle.UserPasswordsData.DeletedPasswords)
            yield return new(TombstoneItemType.Password, item.Id, item.Version, item.CausalReference,
                () => Remove(bundle.UserPasswordsData.DeletedPasswords, item, UserDataBlobKind.Passwords));
        foreach (var item in bundle.UserPasswordsData.DeletedTags)
            yield return new(TombstoneItemType.PasswordTag, item.Id, item.Version, item.CausalReference,
                () => Remove(bundle.UserPasswordsData.DeletedTags, item, UserDataBlobKind.Passwords));
        foreach (var item in bundle.UserPasswordsData.DeletedCustomColors)
            yield return new(TombstoneItemType.CustomUserColor, item.Id, item.Version, item.CausalReference,
                () => Remove(bundle.UserPasswordsData.DeletedCustomColors, item, UserDataBlobKind.Passwords));
        foreach (var item in bundle.UserDevicesData.DeletedDevices)
            yield return new(TombstoneItemType.EncryptedUserDevice, item.Id, item.Version, item.CausalReference,
                () => Remove(bundle.UserDevicesData.DeletedDevices, item, UserDataBlobKind.Devices));
    }

    private static UserDataBlobKind Remove<T>(List<T> list, T item, UserDataBlobKind blob)
    {
        if (!list.Remove(item))
            throw new InvalidOperationException("A tombstone selected for collection was no longer present.");
        return blob;
    }

    private static async Task<TombstoneGarbageCollectionResult> RollbackBlockedAsync(
        IUnitOfWorkTransaction transaction,
        Guid userId,
        TombstoneGarbageCollectionReason reason,
        CancellationToken ct,
        int retainedCount = 0)
    {
        await transaction.RollbackAsync(ct);
        return TombstoneGarbageCollectionResult.Blocked(userId, reason, retainedCount);
    }

    internal sealed record TombstoneDescriptor(
        TombstoneItemType ItemType,
        Guid ItemId,
        SyncVersionStamp Version,
        TombstoneCausalReference CausalReference,
        Func<UserDataBlobKind> Remove);

    internal sealed record StabilityEvaluation(
        TombstoneGarbageCollectionReason Reason,
        Guid? BlockingDeviceId = null,
        Guid? BlockingOriginInstanceId = null,
        long? BlockingKeyEpoch = null);

    private sealed record ReceiptLoadResult(
        IReadOnlyDictionary<(Guid DeviceId, Guid OriginInstanceId), List<UserSnapshotEnvelope>> Receipts,
        TombstoneGarbageCollectionReason? FailureReason);
}
