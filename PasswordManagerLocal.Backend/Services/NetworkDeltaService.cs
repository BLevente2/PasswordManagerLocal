using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Orchestrates incoming and outgoing network deltas. Cryptographic validation, replay detection,
/// model-specific mutation, and post-apply lifecycle work are delegated to focused services.
/// </summary>
public sealed class NetworkDeltaService : INetworkDeltaService
{
    private readonly IOutgoingDeltaBuilderService _outgoingDeltaBuilder;
    private readonly INetworkDeltaProtocolService _protocol;
    private readonly INetworkDeltaReplayService _replay;
    private readonly INetworkDeltaPayloadApplierService _payloadApplier;
    private readonly INetworkDeltaLifecycleService _lifecycle;
    private readonly IDeviceIdentityService _identity;
    private readonly IUnitOfWork _uow;

    public NetworkDeltaService(
        IOutgoingDeltaBuilderService outgoingDeltaBuilder,
        INetworkDeltaProtocolService protocol,
        INetworkDeltaReplayService replay,
        INetworkDeltaPayloadApplierService payloadApplier,
        INetworkDeltaLifecycleService lifecycle,
        IDeviceIdentityService identity,
        IUnitOfWork uow)
    {
        _outgoingDeltaBuilder = outgoingDeltaBuilder;
        _protocol = protocol;
        _replay = replay;
        _payloadApplier = payloadApplier;
        _lifecycle = lifecycle;
        _identity = identity;
        _uow = uow;
    }

    public Task<NetworkDelta> BuildAsync(SyncItem item, Device device, CancellationToken ct = default) =>
        _outgoingDeltaBuilder.BuildAsync(item, device, ct);

    public async Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            throw new SyncRouteDisabledException("Local synchronization is disabled.");

        var validated = await _protocol.ValidateAndReadAsync(delta, ct);
        var sourceDevice = validated.SourceDevice;
        var payload = validated.Payload;

        if (await _lifecycle.TryAcknowledgeAlreadyDeletedUserAsync(payload, sourceDevice, delta.Ts, ct))
        {
            await _uow.SaveChangesAsync(ct);
            return delta.Ts;
        }

        await _protocol.ValidateSourceAuthorizationAsync(sourceDevice, payload, ct);

        if (await _replay.IsBlockedByNewerTombstoneAsync(payload, delta.Ts, ct))
        {
            await _lifecycle.TouchSourceDeviceAsync(sourceDevice, ct);
            await _uow.SaveChangesAsync(ct);
            return delta.Ts;
        }

        if (await _replay.IsAlreadyAppliedAsync(payload, delta.Ts, ct))
        {
            await _lifecycle.TouchSourceDeviceAsync(sourceDevice, ct);
            await _uow.SaveChangesAsync(ct);
            return delta.Ts;
        }

        var applied = await _payloadApplier.ApplyAsync(payload, sourceDevice.Id, delta.Ts, ct);

        await _lifecycle.BeforeSaveAsync(payload, sourceDevice, applied, delta.Ts, ct);
        await _uow.SaveChangesAsync(ct);
        await _lifecycle.AfterSaveAsync(payload, sourceDevice, applied, delta.Ts, ct);

        return delta.Ts;
    }
}
