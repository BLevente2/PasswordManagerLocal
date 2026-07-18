using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Orchestrates incoming and outgoing network deltas. Ordinary user-state updates are durably
/// stored as immutable pending snapshots and are never applied through blind encrypted replacement.
/// </summary>
public sealed class NetworkDeltaService : INetworkDeltaService
{
    private readonly IOutgoingDeltaBuilderService _outgoingDeltaBuilder;
    private readonly INetworkDeltaProtocolService _protocol;
    private readonly INetworkDeltaReplayService _replay;
    private readonly INetworkDeltaPayloadApplierService _payloadApplier;
    private readonly INetworkDeltaLifecycleService _lifecycle;
    private readonly IUserSnapshotInboxService? _snapshotInbox;
    private readonly IUserSnapshotMergeCoordinator? _mergeCoordinator;
    private readonly IUserSyncKeyResolverService? _keyResolver;
    private readonly IUserRepository? _users;
    private readonly IUserRevisionKnowledgeRepository? _revisionKnowledge;
    private readonly IAuthService? _auth;
    private readonly IDeviceIdentityService _identity;
    private readonly IUnitOfWork _uow;

    public NetworkDeltaService(
        IOutgoingDeltaBuilderService outgoingDeltaBuilder,
        INetworkDeltaProtocolService protocol,
        INetworkDeltaReplayService replay,
        INetworkDeltaPayloadApplierService payloadApplier,
        INetworkDeltaLifecycleService lifecycle,
        IDeviceIdentityService identity,
        IUnitOfWork uow,
        IUserSnapshotInboxService? snapshotInbox = null,
        IUserSnapshotMergeCoordinator? mergeCoordinator = null,
        IUserSyncKeyResolverService? keyResolver = null,
        IUserRepository? users = null,
        IUserRevisionKnowledgeRepository? revisionKnowledge = null,
        IAuthService? auth = null)
    {
        _outgoingDeltaBuilder = outgoingDeltaBuilder;
        _protocol = protocol;
        _replay = replay;
        _payloadApplier = payloadApplier;
        _lifecycle = lifecycle;
        _identity = identity;
        _uow = uow;
        _snapshotInbox = snapshotInbox;
        _mergeCoordinator = mergeCoordinator;
        _keyResolver = keyResolver;
        _users = users;
        _revisionKnowledge = revisionKnowledge;
        _auth = auth;
    }

    public Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default) =>
        _outgoingDeltaBuilder.BuildAsync(item, device, ct);

    public async Task<NetworkDeltaApplyResult> ApplyAsync(NetworkDelta delta, CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            throw new SyncRouteDisabledException("Local synchronization is disabled.");

        var validated = await _protocol.ValidateAndReadAsync(delta, ct);
        var sourceDevice = validated.SourceDevice;
        var payload = validated.Payload;

        if (await _lifecycle.TryAcknowledgeAlreadyDeletedUserAsync(payload, sourceDevice, delta.Ts, ct))
        {
            await _uow.SaveChangesAsync(ct);
            var deletedUserReceipt = payload.UserSnapshot is null
                ? null
                : new UserSnapshotReceiptResult(
                    payload.UserSnapshot.UserId,
                    payload.UserSnapshot.OriginDeviceId,
                    payload.UserSnapshot.OriginInstanceId,
                    payload.UserSnapshot.OriginRevision,
                    payload.UserSnapshot.SnapshotHash.ToArray(),
                    UserSnapshotReceiptState.ObsoleteRevision,
                    "The account was authoritatively deleted locally; the ordinary snapshot cannot recreate it.");
            return new NetworkDeltaApplyResult(delta.Ts, deletedUserReceipt);
        }

        await _protocol.ValidateSourceAuthorizationAsync(sourceDevice, payload, ct);

        if (payload.ModelType == SyncModelType.User &&
            payload.ChangeType != SyncChangeType.Deleted)
        {
            return await StoreAndOptionallyMergeUserSnapshotAsync(payload, sourceDevice, delta.Ts, ct);
        }

        if (await _replay.IsBlockedByNewerTombstoneAsync(payload, delta.Ts, ct))
        {
            await _lifecycle.TouchSourceDeviceAsync(sourceDevice, ct);
            await _uow.SaveChangesAsync(ct);
            return new NetworkDeltaApplyResult(delta.Ts);
        }

        if (await _replay.IsAlreadyAppliedAsync(payload, delta.Ts, ct))
        {
            await _lifecycle.TouchSourceDeviceAsync(sourceDevice, ct);
            await _uow.SaveChangesAsync(ct);
            return new NetworkDeltaApplyResult(delta.Ts);
        }

        var applied = await _payloadApplier.ApplyAsync(payload, sourceDevice.Id, delta.Ts, ct);

        await _lifecycle.BeforeSaveAsync(payload, sourceDevice, applied, delta.Ts, ct);
        await _uow.SaveChangesAsync(ct);
        await _lifecycle.AfterSaveAsync(payload, sourceDevice, applied, delta.Ts, ct);

        return new NetworkDeltaApplyResult(delta.Ts);
    }

    private async Task<NetworkDeltaApplyResult> StoreAndOptionallyMergeUserSnapshotAsync(
        SyncDeltaPayload payload,
        Device sourceDevice,
        long transportTimestamp,
        CancellationToken ct)
    {
        var envelope = payload.UserSnapshot
            ?? throw new InvalidDataException("User snapshot envelope is missing.");

        var inbox = _snapshotInbox ?? throw new InvalidOperationException("User snapshot inbox is not configured.");
        var mergeCoordinator = _mergeCoordinator ?? throw new InvalidOperationException("User snapshot merge coordinator is not configured.");
        var keyResolver = _keyResolver ?? throw new InvalidOperationException("User sync key resolver is not configured.");
        var users = _users ?? throw new InvalidOperationException("User repository is not configured for snapshot synchronization.");

        var receipt = await inbox.StoreAsync(envelope, sourceDevice.Id, ct);
        await _lifecycle.TouchSourceDeviceAsync(sourceDevice, ct);
        await _uow.SaveChangesAsync(ct);

        var durable = receipt.State is
            UserSnapshotReceiptState.StoredPending or
            UserSnapshotReceiptState.ReplacedOlderPending or
            UserSnapshotReceiptState.AlreadyStored or
            UserSnapshotReceiptState.ObsoleteRevision;
        if (!durable)
            return new NetworkDeltaApplyResult(transportTimestamp, receipt);

        var user = await users.GetByIdAsync(envelope.UserId, ct);
        if (user is null || !keyResolver.TryResolve(user, out var key) || key is null)
            return new NetworkDeltaApplyResult(transportTimestamp, receipt);

        using (key)
        {
            if (!await mergeCoordinator.TryMergePendingAsync(envelope.UserId, key, ct))
                return new NetworkDeltaApplyResult(transportTimestamp, receipt);
        }

        // A batch merge can succeed because of a different origin while this exact candidate
        // was quarantined. Report MergedImmediately only when durable knowledge proves that the
        // acknowledged origin revision is actually covered by canonical state.
        if (_revisionKnowledge is not null)
        {
            var knowledge = await _revisionKnowledge.GetAsync(
                envelope.UserId,
                envelope.OriginDeviceId,
                envelope.OriginInstanceId,
                envelope.UserKeyEpoch,
                ct);
            if (knowledge is null || knowledge.HighestMergedRevision < envelope.OriginRevision)
                return new NetworkDeltaApplyResult(transportTimestamp, receipt);
        }

        var refreshedUser = await users.GetByIdWithRelationsAsync(envelope.UserId, ct);
        if (refreshedUser is not null && _auth is not null)
            await _auth.RefreshSyncedUserSessionsAsync(refreshedUser, ct);

        var mergedReceipt = receipt with
        {
            State = UserSnapshotReceiptState.MergedImmediately,
            Detail = "The pending snapshot was durably stored and then incorporated into canonical state."
        };
        return new NetworkDeltaApplyResult(transportTimestamp, mergedReceipt);
    }
}
