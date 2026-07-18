using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserSnapshotPublisherService : IUserSnapshotPublisherService
{
    private readonly IUserSyncSnapshotRepository _snapshots;
    private readonly IUserSyncStateRepository _states;
    private readonly IUserRevisionKnowledgeRepository _knowledge;
    private readonly IDeviceIdentityService _identity;
    private readonly IUnitOfWork _uow;
    private readonly IUserLifecycleCoordinator _lifecycle;

    public UserSnapshotPublisherService(
        IUserSyncSnapshotRepository snapshots,
        IUserSyncStateRepository states,
        IUserRevisionKnowledgeRepository knowledge,
        IDeviceIdentityService identity,
        IUnitOfWork uow,
        IUserLifecycleCoordinator lifecycle)
    {
        _snapshots = snapshots;
        _states = states;
        _knowledge = knowledge;
        _identity = identity;
        _uow = uow;
        _lifecycle = lifecycle;
    }

    public Task<UserSyncSnapshot?> GetLatestAsync(Guid userId, long userKeyEpoch, CancellationToken ct = default) =>
        _snapshots.GetLatestLocalAsync(userId, _identity.LocalDeviceId, _identity.OriginInstanceId, userKeyEpoch, ct);

    public Task<UserSyncSnapshot> GetOrCreateAsync(User user, CancellationToken ct = default)
    {
        if (user.UId == Guid.Empty)
            throw new InvalidOperationException("Cannot publish a snapshot for an invalid user.");

        return _lifecycle.ExecuteAsync(user.UId, token => GetOrCreateCoreAsync(user, token), ct);
    }

    private async Task<UserSyncSnapshot> GetOrCreateCoreAsync(User user, CancellationToken ct)
    {
        if (user.UId == Guid.Empty)
            throw new InvalidOperationException("Cannot publish a snapshot for an invalid user.");

        var state = await _states.GetAsync(user.UId, ct);
        var isNewState = state is null;
        if (state is null)
        {
            state = new UserSyncState
            {
                UserId = user.UId,
                LocalOriginInstanceId = _identity.OriginInstanceId,
                NextOriginRevision = 1,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await _states.AddAsync(state, ct);
        }
        else if (state.LocalOriginInstanceId != _identity.OriginInstanceId)
        {
            state.LocalOriginInstanceId = _identity.OriginInstanceId;
            state.NextOriginRevision = 1;
            state.LastPublishedContentHash = [];
            state.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            _states.Update(state);
        }

        var remoteCoverage = (await _knowledge.ListAsync(user.UId, user.KeyEpoch, ct))
            .Where(item =>
                item.HighestMergedRevision > 0 &&
                (item.OriginDeviceId != _identity.LocalDeviceId ||
                 item.OriginInstanceId != _identity.OriginInstanceId))
            .Select(item => new UserSnapshotCoverageEntry
            {
                OriginDeviceId = item.OriginDeviceId,
                OriginInstanceId = item.OriginInstanceId,
                UserKeyEpoch = item.UserKeyEpoch,
                OriginRevision = item.HighestMergedRevision
            })
            .ToList();

        // The local origin revision is an identity of the snapshot, not an input that should
        // force another revision every time GetOrCreateAsync is called. Only canonical bytes,
        // membership/key epochs, and newly merged remote knowledge participate in reuse.
        var contentHash = CalculatePublishedContentHash(user, remoteCoverage);
        var existing = await GetLatestAsync(user.UId, user.KeyEpoch, ct);
        if (existing is not null && Hashing.Verify(state.LastPublishedContentHash, contentHash))
            return existing;

        var revision = state.NextOriginRevision;
        if (revision <= 0)
            throw new InvalidOperationException("The next local user snapshot revision is invalid.");

        var coverage = remoteCoverage
            .Append(new UserSnapshotCoverageEntry
            {
                OriginDeviceId = _identity.LocalDeviceId,
                OriginInstanceId = _identity.OriginInstanceId,
                UserKeyEpoch = user.KeyEpoch,
                OriginRevision = revision
            })
            .OrderBy(item => item.OriginDeviceId)
            .ThenBy(item => item.OriginInstanceId)
            .ThenBy(item => item.UserKeyEpoch)
            .ToList();

        var createdAtUtc = DateTimeOffset.UtcNow;
        var envelope = new UserSnapshotEnvelope
        {
            UserId = user.UId,
            OriginDeviceId = _identity.LocalDeviceId,
            OriginInstanceId = _identity.OriginInstanceId,
            OriginRevision = revision,
            UserKeyEpoch = user.KeyEpoch,
            MembershipEpoch = user.MembershipEpoch,
            CreatedAtUtc = createdAtUtc,
            User = CreateUserPayload(user, createdAtUtc.ToUnixTimeMilliseconds()),
            Coverage = coverage
        };
        UserSnapshotEnvelopeUtil.FillOriginAuthentication(envelope, _identity);

        var serialized = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            BackendJsonSerializerContext.Default.UserSnapshotEnvelope);

        if (serialized.Length == 0 || serialized.Length > Constants.SyncConstants.MaxUserSnapshotEnvelopeBytes)
            throw new InvalidDataException("The user snapshot envelope size is invalid.");

        var row = existing ?? new UserSyncSnapshot
        {
            UserId = envelope.UserId,
            OriginDeviceId = envelope.OriginDeviceId,
            OriginInstanceId = envelope.OriginInstanceId,
            UserKeyEpoch = envelope.UserKeyEpoch,
            Status = UserSyncSnapshotStatus.LocalPublished
        };

        CopyEnvelopeToRow(envelope, serialized, row);
        row.ReceivedAtUtc = createdAtUtc;
        row.LastReceivedFromDeviceId = null;
        row.Status = UserSyncSnapshotStatus.LocalPublished;
        row.QuarantineReason = null;
        row.ConflictingSnapshotHash = null;

        if (existing is null)
            await _snapshots.AddAsync(row, ct);
        else
            _snapshots.Update(row);

        state.NextOriginRevision = checked(revision + 1);
        state.LastPublishedContentHash = contentHash;
        state.LastUpdatedAtUtc = createdAtUtc;
        if (!isNewState)
            _states.Update(state);

        var localKnowledge = await _knowledge.GetAsync(
            user.UId,
            _identity.LocalDeviceId,
            _identity.OriginInstanceId,
            user.KeyEpoch,
            ct);
        var isNewKnowledge = localKnowledge is null;
        if (localKnowledge is null)
        {
            localKnowledge = new UserRevisionKnowledge
            {
                UserId = user.UId,
                OriginDeviceId = _identity.LocalDeviceId,
                OriginInstanceId = _identity.OriginInstanceId,
                UserKeyEpoch = user.KeyEpoch
            };
            await _knowledge.AddAsync(localKnowledge, ct);
        }

        localKnowledge.HighestStoredRevision = revision;
        localKnowledge.HighestStoredSnapshotHash = envelope.SnapshotHash.ToArray();
        localKnowledge.HighestMergedRevision = revision;
        localKnowledge.LastUpdatedAtUtc = createdAtUtc;
        if (!isNewKnowledge)
            _knowledge.Update(localKnowledge);
        await _uow.SaveChangesAsync(ct);
        return row;
    }

    private UserSyncPayload CreateUserPayload(User user, long timestamp)
    {
        foreach (var link in user.UserDevices)
            link.VerifyIntegrity();

        var payload = new UserSyncPayload
        {
            UId = user.UId,
            UsernameHash = user.UsernameHash.ToArray(),
            UsernameSalt = user.UsernameSalt.ToArray(),
            PasswordSalt = user.PasswordSalt.ToArray(),
            EncryptedPayload = user.EncryptedPayload.ToArray(),
            EncryptedGeneralUserDataPayload = user.EncryptedGeneralUserDataPayload.ToArray(),
            EncryptedUserPasswordsDataPayload = user.EncryptedUserPasswordsDataPayload.ToArray(),
            EncryptedUserDevicesDataPayload = user.EncryptedUserDevicesDataPayload.ToArray(),
            UserDataLastModifiedAt = user.UserDataLastModifiedAt,
            GeneralUserDataLastModifiedAt = user.GeneralUserDataLastModifiedAt,
            UserPasswordsDataLastModifiedAt = user.UserPasswordsDataLastModifiedAt,
            UserDevicesDataLastModifiedAt = user.UserDevicesDataLastModifiedAt,
            GroupIds = user.Groups.Select(group => group.Id).Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToList(),
            DeviceIds = user.UserDevices
                .Where(link => !link.IsDeleted)
                .Select(link => link.DeviceId)
                .Append(_identity.LocalDeviceId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToList()
        };
        payload.IntegrityHash = SyncCryptoUtil.CalculateUserHash(payload, timestamp);
        return payload;
    }

    private static byte[] CalculatePublishedContentHash(User user, IReadOnlyList<UserSnapshotCoverageEntry> coverage) =>
        Hashing.SHA256Hash(hash =>
        {
            hash.WriteString("PasswordManagerLocal.Backend.UserSnapshot.PublishedContent.v1");
            hash.Write(user.UId);
            hash.Write(user.KeyEpoch);
            hash.Write(user.MembershipEpoch);
            hash.WriteBytes(user.UsernameHash);
            hash.WriteBytes(user.UsernameSalt);
            hash.WriteBytes(user.PasswordSalt);
            hash.WriteBytes(user.EncryptedPayload);
            hash.WriteBytes(user.EncryptedGeneralUserDataPayload);
            hash.WriteBytes(user.EncryptedUserPasswordsDataPayload);
            hash.WriteBytes(user.EncryptedUserDevicesDataPayload);
            hash.Write(user.UserDataLastModifiedAt);
            hash.Write(user.GeneralUserDataLastModifiedAt);
            hash.Write(user.UserPasswordsDataLastModifiedAt);
            hash.Write(user.UserDevicesDataLastModifiedAt);

            foreach (var groupId in user.Groups.Select(group => group.Id).Distinct().OrderBy(id => id))
                hash.Write(groupId);
            foreach (var deviceId in user.UserDevices.Where(link => !link.IsDeleted).Select(link => link.DeviceId).Distinct().OrderBy(id => id))
                hash.Write(deviceId);
            foreach (var item in coverage.OrderBy(item => item.OriginDeviceId).ThenBy(item => item.OriginInstanceId).ThenBy(item => item.UserKeyEpoch))
            {
                hash.Write(item.OriginDeviceId);
                hash.Write(item.OriginInstanceId);
                hash.Write(item.UserKeyEpoch);
                hash.Write(item.OriginRevision);
            }
        });

    private static void CopyEnvelopeToRow(UserSnapshotEnvelope envelope, byte[] serialized, UserSyncSnapshot row)
    {
        row.OriginRevision = envelope.OriginRevision;
        row.MembershipEpoch = envelope.MembershipEpoch;
        row.CreatedAtUtc = envelope.CreatedAtUtc;
        row.SnapshotHash = envelope.SnapshotHash.ToArray();
        row.OriginSignPublicKey = envelope.OriginSignPublicKey.ToArray();
        row.OriginSignature = envelope.OriginSignature.ToArray();
        row.EnvelopePayload = serialized;
    }
}
