using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserSnapshotInboxService : IUserSnapshotInboxService
{
    private readonly IUserRepository _users;
    private readonly IUserSyncSnapshotRepository _snapshots;
    private readonly IUserRevisionKnowledgeRepository _knowledge;
    private readonly IDeviceIdentityService _identity;
    private readonly IUnitOfWork _uow;
    private readonly IUserLifecycleCoordinator _lifecycle;
    private readonly IUserMembershipAuthorizationService _membershipAuthorization;
    private readonly IDeletedUserBarrierRepository? _deletionBarriers;

    public UserSnapshotInboxService(
        IUserRepository users,
        IUserSyncSnapshotRepository snapshots,
        IUserRevisionKnowledgeRepository knowledge,
        IDeviceIdentityService identity,
        IUnitOfWork uow,
        IUserLifecycleCoordinator lifecycle,
        IUserMembershipAuthorizationService membershipAuthorization,
        IDeletedUserBarrierRepository? deletionBarriers = null)
    {
        _users = users;
        _snapshots = snapshots;
        _knowledge = knowledge;
        _identity = identity;
        _uow = uow;
        _lifecycle = lifecycle;
        _membershipAuthorization = membershipAuthorization;
        _deletionBarriers = deletionBarriers;
    }

    public async Task<UserSnapshotReceiptResult> StoreAsync(
        UserSnapshotEnvelope envelope,
        Guid transportPeerDeviceId,
        CancellationToken ct = default)
    {
        UserSnapshotEnvelopeUtil.ValidateStructureAndHash(envelope);

        return await _lifecycle.ExecuteAsync(
            envelope.UserId,
            async token =>
            {
                var receipt = await StoreCoreAsync(envelope, transportPeerDeviceId, token);
                await _uow.SaveChangesAsync(token);
                return receipt;
            },
            ct);
    }

    private async Task<UserSnapshotReceiptResult> StoreCoreAsync(
        UserSnapshotEnvelope envelope,
        Guid transportPeerDeviceId,
        CancellationToken ct)
    {
        // Expected replays after deletion are acknowledged explicitly before authorization,
        // revision comparison, or pending-row replacement can recreate state.
        if (_deletionBarriers is not null && await _deletionBarriers.ExistsAsync(envelope.UserId, ct))
        {
            return Receipt(envelope, UserSnapshotReceiptState.RejectedAccountDeleted,
                "The account identity is permanently deleted.");
        }

        var user = await _users.GetByIdAsync(envelope.UserId, ct);
        if (user is null)
            return Receipt(envelope, UserSnapshotReceiptState.Rejected, "The user does not exist locally.");

        try
        {
            await _membershipAuthorization.VerifySnapshotAuthorAsync(envelope, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException)
        {
            return Receipt(envelope, UserSnapshotReceiptState.Rejected, ex.Message);
        }

        if (envelope.UserKeyEpoch < user.KeyEpoch)
            return Receipt(envelope, UserSnapshotReceiptState.ObsoleteRevision, "The snapshot uses an obsolete key epoch.");
        if (envelope.UserKeyEpoch > user.KeyEpoch)
            return Receipt(envelope, UserSnapshotReceiptState.WrongKeyEpoch, "The snapshot uses a newer key epoch requiring an explicit replacement operation.");
        if (envelope.MembershipEpoch > user.MembershipEpoch)
            return Receipt(envelope, UserSnapshotReceiptState.WrongMembershipEpoch, "The snapshot uses a newer membership epoch whose predecessor operation is missing.");

        if (envelope.OriginDeviceId == _identity.LocalDeviceId &&
            envelope.OriginInstanceId == _identity.OriginInstanceId)
        {
            return Receipt(
                envelope,
                UserSnapshotReceiptState.Quarantined,
                "A transport peer cannot introduce revisions for the current local origin identity.");
        }

        var knowledge = await _knowledge.GetAsync(
            envelope.UserId,
            envelope.OriginDeviceId,
            envelope.OriginInstanceId,
            envelope.UserKeyEpoch,
            ct);
        var existing = await _snapshots.GetAsync(
            envelope.UserId,
            envelope.OriginDeviceId,
            envelope.OriginInstanceId,
            envelope.UserKeyEpoch,
            ct);

        if (existing is not null && existing.Status == UserSyncSnapshotStatus.Quarantined)
            return Receipt(envelope, UserSnapshotReceiptState.Quarantined, existing.QuarantineReason ?? "This origin is quarantined.");

        if (knowledge is not null &&
            knowledge.HighestStoredRevision == envelope.OriginRevision &&
            knowledge.HighestStoredSnapshotHash.Length != 0 &&
            !Hashing.Verify(knowledge.HighestStoredSnapshotHash, envelope.SnapshotHash))
        {
            const string reason = "The same origin revision was received with a different snapshot hash.";
            if (existing is null)
            {
                existing = await CreateQuarantinedEvidenceAsync(
                    envelope,
                    transportPeerDeviceId,
                    knowledge.HighestStoredSnapshotHash,
                    reason,
                    ct);
            }
            else
            {
                Quarantine(existing, envelope.SnapshotHash, transportPeerDeviceId, reason);
            }

            return Receipt(envelope, UserSnapshotReceiptState.Quarantined, reason);
        }

        if (knowledge is not null && knowledge.HighestMergedRevision >= envelope.OriginRevision)
            return Receipt(envelope, UserSnapshotReceiptState.ObsoleteRevision, "The revision is already included in canonical state.");
        if (knowledge is not null && knowledge.HighestStoredRevision > envelope.OriginRevision)
            return Receipt(envelope, UserSnapshotReceiptState.ObsoleteRevision, "A newer revision from this origin is already known as durably stored.");

        if (existing is null && knowledge is not null && knowledge.HighestStoredRevision == envelope.OriginRevision)
        {
            const string reason = "Stored-revision knowledge exists without the corresponding pending snapshot row.";
            existing = await CreateQuarantinedEvidenceAsync(
                envelope,
                transportPeerDeviceId,
                knowledge.HighestStoredSnapshotHash,
                reason,
                ct);
            return Receipt(envelope, UserSnapshotReceiptState.Quarantined, reason);
        }

        if (existing is not null)
        {
            if (existing.OriginRevision > envelope.OriginRevision)
                return Receipt(envelope, UserSnapshotReceiptState.ObsoleteRevision, "A newer revision from this origin is already stored.");

            if (existing.OriginRevision == envelope.OriginRevision)
            {
                if (Hashing.Verify(existing.SnapshotHash, envelope.SnapshotHash))
                {
                    existing.ReceivedAtUtc = DateTimeOffset.UtcNow;
                    existing.LastReceivedFromDeviceId = transportPeerDeviceId;
                    _snapshots.Update(existing);
                    return Receipt(envelope, UserSnapshotReceiptState.AlreadyStored);
                }

                const string reason = "The same origin revision was received with a different snapshot hash.";
                Quarantine(existing, envelope.SnapshotHash, transportPeerDeviceId, reason);
                return Receipt(envelope, UserSnapshotReceiptState.Quarantined, reason);
            }

        }

        byte[] serialized;
        try
        {
            serialized = SerializeEnvelope(envelope);
        }
        catch (InvalidDataException ex)
        {
            return Receipt(envelope, UserSnapshotReceiptState.Rejected, ex.Message);
        }

        var now = DateTimeOffset.UtcNow;
        var row = existing ?? new UserSyncSnapshot
        {
            UserId = envelope.UserId,
            OriginDeviceId = envelope.OriginDeviceId,
            OriginInstanceId = envelope.OriginInstanceId,
            UserKeyEpoch = envelope.UserKeyEpoch
        };
        row.OriginRevision = envelope.OriginRevision;
        row.MembershipEpoch = envelope.MembershipEpoch;
        row.CreatedAtUtc = envelope.CreatedAtUtc;
        row.ReceivedAtUtc = now;
        row.LastReceivedFromDeviceId = transportPeerDeviceId;
        row.SnapshotHash = envelope.SnapshotHash.ToArray();
        row.OriginSignPublicKey = envelope.OriginSignPublicKey.ToArray();
        row.OriginSignature = envelope.OriginSignature.ToArray();
        row.EnvelopePayload = serialized;
        row.Status = UserSyncSnapshotStatus.Pending;
        row.QuarantineReason = null;
        row.ConflictingSnapshotHash = null;

        if (existing is null)
            await _snapshots.AddAsync(row, ct);
        else
            _snapshots.Update(row);

        var isNewKnowledge = knowledge is null;
        knowledge ??= new UserRevisionKnowledge
        {
            UserId = envelope.UserId,
            OriginDeviceId = envelope.OriginDeviceId,
            OriginInstanceId = envelope.OriginInstanceId,
            UserKeyEpoch = envelope.UserKeyEpoch
        };
        knowledge.HighestStoredRevision = envelope.OriginRevision;
        knowledge.HighestStoredSnapshotHash = envelope.SnapshotHash.ToArray();
        knowledge.LastUpdatedAtUtc = now;
        if (isNewKnowledge)
            await _knowledge.AddAsync(knowledge, ct);
        else
            _knowledge.Update(knowledge);

        return Receipt(
            envelope,
            existing is null ? UserSnapshotReceiptState.StoredPending : UserSnapshotReceiptState.ReplacedOlderPending);
    }

    private async Task<UserSyncSnapshot> CreateQuarantinedEvidenceAsync(
        UserSnapshotEnvelope envelope,
        Guid transportPeerDeviceId,
        byte[] conflictingHash,
        string reason,
        CancellationToken ct)
    {
        var serialized = SerializeEnvelope(envelope);
        var row = new UserSyncSnapshot
        {
            UserId = envelope.UserId,
            OriginDeviceId = envelope.OriginDeviceId,
            OriginInstanceId = envelope.OriginInstanceId,
            OriginRevision = envelope.OriginRevision,
            UserKeyEpoch = envelope.UserKeyEpoch,
            MembershipEpoch = envelope.MembershipEpoch,
            CreatedAtUtc = envelope.CreatedAtUtc,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            LastReceivedFromDeviceId = transportPeerDeviceId,
            SnapshotHash = envelope.SnapshotHash.ToArray(),
            OriginSignPublicKey = envelope.OriginSignPublicKey.ToArray(),
            OriginSignature = envelope.OriginSignature.ToArray(),
            EnvelopePayload = serialized,
            Status = UserSyncSnapshotStatus.Quarantined,
            QuarantineReason = reason,
            ConflictingSnapshotHash = conflictingHash.ToArray()
        };
        await _snapshots.AddAsync(row, ct);
        return row;
    }

    private void Quarantine(
        UserSyncSnapshot row,
        byte[] conflictingHash,
        Guid transportPeerDeviceId,
        string reason)
    {
        row.Status = UserSyncSnapshotStatus.Quarantined;
        row.QuarantineReason = reason;
        row.ConflictingSnapshotHash = conflictingHash.ToArray();
        row.ReceivedAtUtc = DateTimeOffset.UtcNow;
        row.LastReceivedFromDeviceId = transportPeerDeviceId;
        _snapshots.Update(row);
    }

    private static byte[] SerializeEnvelope(UserSnapshotEnvelope envelope)
    {
        var serialized = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            BackendJsonSerializerContext.Default.UserSnapshotEnvelope);
        if (serialized.Length == 0 || serialized.Length > Constants.SyncConstants.MaxUserSnapshotEnvelopeBytes)
            throw new InvalidDataException("The snapshot envelope size is invalid.");
        return serialized;
    }

    private static UserSnapshotReceiptResult Receipt(
        UserSnapshotEnvelope envelope,
        UserSnapshotReceiptState state,
        string? detail = null) =>
        new(
            envelope.UserId,
            envelope.OriginDeviceId,
            envelope.OriginInstanceId,
            envelope.OriginRevision,
            envelope.SnapshotHash.ToArray(),
            state,
            detail);
}
