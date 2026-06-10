using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Sync;
using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalBackend.Services.Grpc;

public sealed class SyncGrpcServiceImpl : SyncGrpc.SyncGrpcBase
{
    private readonly GrpcRootServiceProvider _root;

    public SyncGrpcServiceImpl(GrpcRootServiceProvider root)
    {
        _root = root;
    }




    public override async Task<HelloReply> Hello(HelloRequest request, ServerCallContext context)
    {
        using var scope = _root.Services.CreateScope();
        Device? remoteDevice = null;

        try
        {
            remoteDevice = await ValidateRemoteDeviceAsync(scope.ServiceProvider, context, request.DeviceId, request.SignPub.ToByteArray(), context.CancellationToken);
            return new HelloReply { Ok = true };
        }
        catch (Exception ex)
        {
            remoteDevice ??= await TryFindRemoteDeviceForInvalidAttemptAsync(scope.ServiceProvider, context, context.CancellationToken);

            if (remoteDevice is not null && !remoteDevice.IsBlocked)
                await RecordInvalidAttemptAsync(scope.ServiceProvider, remoteDevice, ex.Message, context.CancellationToken);

            return new HelloReply { Ok = false };
        }
    }


    public override async Task<Ack> PushDelta(IAsyncStreamReader<DeltaChunk> requestStream, ServerCallContext context)
    {
        using var scope = _root.Services.CreateScope();
        Device? remoteDevice = null;

        try
        {
            remoteDevice = await ValidateRemoteDeviceAsync(scope.ServiceProvider, context, null, null, context.CancellationToken);
            var applier = scope.ServiceProvider.GetRequiredService<IIncomingDeltaApplierService>();
            var deviceSecurity = scope.ServiceProvider.GetRequiredService<IDeviceSecurityService>();
            var identity = scope.ServiceProvider.GetRequiredService<IDeviceIdentityService>();

            var lastSyncedTs = 0L;
            var count = 0;

            await foreach (var chunk in requestStream.ReadAllAsync(context.CancellationToken))
            {
                count++;
                if (count > SyncConstants.MaxIncomingDeltaCountPerCall)
                    throw new RpcException(new Status(StatusCode.ResourceExhausted, "Too many deltas in one sync call."));

                var delta = DeltaMapping.FromProto(chunk);
                ValidateDeltaTransport(delta, remoteDevice, identity.LocalDeviceId);

                var appliedTs = await applier.ApplyAsync(delta, context.CancellationToken);
                if (appliedTs > lastSyncedTs)
                    lastSyncedTs = appliedTs;
            }

            await deviceSecurity.ResetInvalidIncomingSyncAsync(remoteDevice, context.CancellationToken);

            return new Ack
            {
                LastSyncedTs = lastSyncedTs
            };
        }
        catch (RpcException ex)
        {
            if (remoteDevice is null)
                remoteDevice = await TryFindRemoteDeviceForInvalidAttemptAsync(scope.ServiceProvider, context, context.CancellationToken);

            if (remoteDevice is not null && !remoteDevice.IsBlocked && IsInvalidIncomingDataStatus(ex.StatusCode))
                await RecordInvalidAttemptAsync(scope.ServiceProvider, remoteDevice, ex.Status.Detail, context.CancellationToken);

            throw;
        }
        catch (InvalidDataException ex)
        {
            if (remoteDevice is not null && !remoteDevice.IsBlocked)
                await RecordInvalidAttemptAsync(scope.ServiceProvider, remoteDevice, ex.Message, context.CancellationToken);

            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            if (remoteDevice is not null && !remoteDevice.IsBlocked)
                await RecordInvalidAttemptAsync(scope.ServiceProvider, remoteDevice, ex.Message, context.CancellationToken);

            throw new RpcException(new Status(StatusCode.PermissionDenied, "Delta is not authorized."));
        }
    }


    private static async Task<Device?> TryFindRemoteDeviceForInvalidAttemptAsync(IServiceProvider services, ServerCallContext context, CancellationToken ct)
    {
        try
        {
            var clientCertificate = context.GetHttpContext().Connection.ClientCertificate;
            if (clientCertificate is null)
                return null;

            var identity = services.GetRequiredService<IDeviceIdentityService>();
            var devices = services.GetRequiredService<IDeviceRepository>();
            var fingerprint = identity.GetFingerprintHex(new X509Certificate2(clientCertificate));

            if (string.Equals(NormalizeFingerprint(fingerprint), NormalizeFingerprint(identity.FingerprintHex), StringComparison.OrdinalIgnoreCase))
                return null;

            return await devices.GetByTlsCertFingerprintWithUserDevicesAsync(fingerprint, ct);
        }
        catch
        {
            return null;
        }
    }


    private static async Task<Device> ValidateRemoteDeviceAsync(
        IServiceProvider services,
        ServerCallContext context,
        string? claimedDeviceId,
        byte[]? claimedSignPublicKey,
        CancellationToken ct)
    {
        var clientCertificate = context.GetHttpContext().Connection.ClientCertificate;
        if (clientCertificate is null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Client certificate is missing."));

        var identity = services.GetRequiredService<IDeviceIdentityService>();
        if (!identity.IsSyncOn)
            throw new RpcException(new Status(StatusCode.Unavailable, "Local synchronization is disabled."));

        var devices = services.GetRequiredService<IDeviceRepository>();

        var fingerprint = identity.GetFingerprintHex(new X509Certificate2(clientCertificate));
        if (string.Equals(NormalizeFingerprint(fingerprint), NormalizeFingerprint(identity.FingerprintHex), StringComparison.OrdinalIgnoreCase))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Local device cannot sync with itself."));

        var remoteDevice = await devices.GetByTlsCertFingerprintWithUserDevicesAsync(fingerprint, ct);
        if (remoteDevice is null)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Remote device is not trusted."));

        if (remoteDevice.IsBlocked || !remoteDevice.IsTrusted)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Remote device is not allowed to sync."));

        if (!remoteDevice.UserDevices.Any(ud => !ud.IsDeleted && ud.IsSyncEnabled))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Remote device is not linked to any active user."));

        if (remoteDevice.SignPublicKey.Length == 0)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Remote device signing key is missing."));

        if (claimedSignPublicKey is not null)
        {
            if (claimedSignPublicKey.Length == 0)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Remote device signing key is missing."));

            if (!remoteDevice.SignPublicKey.SequenceEqual(claimedSignPublicKey))
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Remote device signing key does not match."));
        }

        if (claimedDeviceId is not null)
        {
            if (string.IsNullOrWhiteSpace(claimedDeviceId))
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Remote device id is missing."));

            if (!string.Equals(claimedDeviceId, BuildDeviceId(remoteDevice.SignPublicKey), StringComparison.OrdinalIgnoreCase))
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Remote device id does not match."));
        }

        if (remoteDevice.Id == identity.LocalDeviceId || identity.SignPublicKey.SequenceEqual(remoteDevice.SignPublicKey))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Local device cannot sync with itself."));

        return remoteDevice;
    }


    private static void ValidateDeltaTransport(NetworkDelta delta, Device remoteDevice, Guid localDeviceId)
    {
        if (string.IsNullOrWhiteSpace(delta.Entity) || delta.Entity.Length > 256)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta entity is invalid."));

        if (delta.Payload.Length == 0 || delta.Payload.Length > SyncConstants.MaxIncomingDeltaPayloadBytes)
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Delta payload size is invalid."));

        if (delta.Ts <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta timestamp is invalid."));

        var maxFutureTs = DateTimeOffset.UtcNow.AddSeconds(SyncConstants.MaxIncomingDeltaFutureSeconds).ToUnixTimeMilliseconds();
        if (delta.Ts > maxFutureTs)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta timestamp is too far in the future."));

        if (delta.SignPub.Length != SyncConstants.SyncDeltaEd25519PublicKeyBytes ||
            delta.Sig.Length != SyncConstants.SyncDeltaEd25519SignatureBytes)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta signature is incomplete."));

        if (!remoteDevice.SignPublicKey.SequenceEqual(delta.SignPub))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Delta signer is not the connected device."));

        if (!string.Equals(delta.DeviceId, BuildDeviceId(delta.SignPub), StringComparison.OrdinalIgnoreCase))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Delta device id is invalid."));

        if (delta.EncryptionVersion != SyncConstants.SyncDeltaEncryptionVersion)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta encryption version is invalid."));

        if (!Guid.TryParseExact(delta.RecipientDeviceId, "N", out var recipientDeviceId) &&
            !Guid.TryParse(delta.RecipientDeviceId, out recipientDeviceId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta recipient is invalid."));

        if (recipientDeviceId != localDeviceId)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Delta recipient is not this device."));

        if (delta.EphemeralPublicKey.Length != SyncConstants.SyncDeltaX25519PublicKeyBytes)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta ephemeral key is invalid."));

        if (delta.Nonce.Length != SyncConstants.SyncDeltaNonceBytes)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta nonce is invalid."));

        if (delta.Tag.Length != SyncConstants.SyncDeltaTagBytes)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta authentication tag is invalid."));

        if (delta.PayloadHash.Length != SyncConstants.SyncDeltaPayloadHashBytes)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Delta payload hash is invalid."));
    }


    private static async Task RecordInvalidAttemptAsync(IServiceProvider services, Device remoteDevice, string reason, CancellationToken ct)
    {
        try
        {
            var deviceSecurity = services.GetRequiredService<IDeviceSecurityService>();
            await deviceSecurity.RecordInvalidIncomingSyncAsync(remoteDevice, reason, ct);
        }
        catch
        {
        }
    }


    private static bool IsInvalidIncomingDataStatus(StatusCode statusCode) =>
        statusCode == StatusCode.InvalidArgument ||
        statusCode == StatusCode.PermissionDenied ||
        statusCode == StatusCode.ResourceExhausted ||
        statusCode == StatusCode.FailedPrecondition;


    private static string BuildDeviceId(byte[] signPublicKey) =>
        Convert.ToHexString(Hashing.SHA256Hash(signPublicKey));


    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
