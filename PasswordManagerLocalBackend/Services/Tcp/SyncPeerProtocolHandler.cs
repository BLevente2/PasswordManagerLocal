using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Sync.Tcp;
using PasswordManagerLocalBackend.Utils;

namespace PasswordManagerLocalBackend.Services.Tcp;

public sealed class SyncPeerProtocolHandler
{
    private readonly IServiceProvider _root;

    public SyncPeerProtocolHandler(IServiceProvider root)
    {
        _root = root;
    }




    public async Task<HelloReply> HelloAsync(HelloRequest request, PeerConnectionContext context, CancellationToken ct)
    {
        using var scope = _root.CreateScope();
        Device? remoteDevice = null;

        try
        {
            remoteDevice = await ValidateRemoteDeviceAsync(scope.ServiceProvider, context, request.DeviceId, request.SignPub.ToByteArray(), ct);
            return new HelloReply { Ok = true };
        }
        catch (Exception ex)
        {
            remoteDevice ??= await TryFindRemoteDeviceForInvalidAttemptAsync(scope.ServiceProvider, context, ct);

            if (remoteDevice is not null && !remoteDevice.IsBlocked)
                await RecordInvalidAttemptAsync(scope.ServiceProvider, remoteDevice, ex.Message, ct);

            return new HelloReply { Ok = false };
        }
    }


    public async Task<Ack> PushDeltaAsync(IReadOnlyList<DeltaChunk> chunks, PeerConnectionContext context, CancellationToken ct)
    {
        using var scope = _root.CreateScope();
        Device? remoteDevice = null;

        try
        {
            remoteDevice = await ValidateRemoteDeviceAsync(scope.ServiceProvider, context, null, null, ct);
            var applier = scope.ServiceProvider.GetRequiredService<IIncomingDeltaApplierService>();
            var deviceSecurity = scope.ServiceProvider.GetRequiredService<IDeviceSecurityService>();
            var identity = scope.ServiceProvider.GetRequiredService<IDeviceIdentityService>();

            if (chunks.Count > SyncConstants.MaxIncomingDeltaCountPerCall)
                throw new SyncProtocolException(SyncProtocolStatusCode.ResourceExhausted, "Too many deltas in one sync call.");

            var lastSyncedTs = 0L;

            foreach (var chunk in chunks)
            {
                var delta = DeltaMapping.FromProto(chunk);
                ValidateDeltaTransport(delta, remoteDevice, identity.LocalDeviceId);

                var appliedTs = await applier.ApplyAsync(delta, ct);
                if (appliedTs > lastSyncedTs)
                    lastSyncedTs = appliedTs;
            }

            await deviceSecurity.ResetInvalidIncomingSyncAsync(remoteDevice, ct);

            return new Ack
            {
                LastSyncedTs = lastSyncedTs
            };
        }
        catch (SyncProtocolException ex)
        {
            if (remoteDevice is null)
                remoteDevice = await TryFindRemoteDeviceForInvalidAttemptAsync(scope.ServiceProvider, context, ct);

            if (remoteDevice is not null && !remoteDevice.IsBlocked && IsInvalidIncomingDataStatus(ex.StatusCode))
                await RecordInvalidAttemptAsync(scope.ServiceProvider, remoteDevice, ex.Message, ct);

            throw;
        }
        catch (InvalidDataException ex)
        {
            if (remoteDevice is not null && !remoteDevice.IsBlocked)
                await RecordInvalidAttemptAsync(scope.ServiceProvider, remoteDevice, ex.Message, ct);

            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            if (remoteDevice is not null && !remoteDevice.IsBlocked)
                await RecordInvalidAttemptAsync(scope.ServiceProvider, remoteDevice, ex.Message, ct);

            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Delta is not authorized.");
        }
    }


    public async Task<GetDeviceEnrollmentInfoReply> GetDeviceEnrollmentInfoAsync(GetDeviceEnrollmentInfoRequest request, CancellationToken ct)
    {
        using var scope = _root.CreateScope();

        try
        {
            DeviceEnrollmentTrace.Info($"Incoming GetDeviceEnrollmentInfo request. Session={request.SessionId}.");
            var identity = scope.ServiceProvider.GetRequiredService<IDeviceIdentityService>();
            if (!identity.IsSyncOn)
            {
                return new GetDeviceEnrollmentInfoReply
                {
                    Ok = false,
                    Error = "Local synchronization is disabled.",
                    ErrorCode = DeviceEnrollmentErrorCode.SyncDisabled.ToString()
                };
            }

            var enrollment = scope.ServiceProvider.GetRequiredService<IDeviceEnrollmentService>();
            var result = await enrollment.GetIncomingEnrollmentInfoAsync(
                request.SessionId,
                request.CodeProof.ToByteArray(),
                ct);

            return new GetDeviceEnrollmentInfoReply
            {
                Ok = result.Ok,
                Error = result.Error ?? string.Empty,
                ErrorCode = result.ErrorCode.ToString(),
                DeviceId = result.DeviceId == Guid.Empty ? string.Empty : result.DeviceId.ToString("N"),
                TlsCertFingerprint = result.TlsCertFingerprint,
                SignPub = ByteString.CopyFrom(result.SignPublicKey),
                AgreementPub = ByteString.CopyFrom(result.AgreementPublicKey)
            };
        }
        catch (Exception ex)
        {
            DeviceEnrollmentTrace.Error($"Incoming GetDeviceEnrollmentInfo request failed. Session={request.SessionId}: {ex.Message}", ex);
            return new GetDeviceEnrollmentInfoReply
            {
                Ok = false,
                Error = ex.Message,
                ErrorCode = DeviceEnrollmentErrorCode.Unknown.ToString()
            };
        }
    }


    public async Task<CompleteDeviceEnrollmentReply> CompleteDeviceEnrollmentStreamAsync(IReadOnlyList<CompleteDeviceEnrollmentChunk> chunks, PeerConnectionContext context, CancellationToken ct)
    {
        using var scope = _root.CreateScope();

        try
        {
            DeviceEnrollmentTrace.Info("Incoming streaming CompleteDeviceEnrollment request started.");
            if (string.IsNullOrWhiteSpace(context.ClientCertificateFingerprint))
                return new CompleteDeviceEnrollmentReply { Ok = false, Error = "Client certificate is missing.", ErrorCode = DeviceEnrollmentErrorCode.NewDeviceRejected.ToString() };

            var identity = scope.ServiceProvider.GetRequiredService<IDeviceIdentityService>();
            if (!identity.IsSyncOn)
                return new CompleteDeviceEnrollmentReply { Ok = false, Error = "Local synchronization is disabled.", ErrorCode = DeviceEnrollmentErrorCode.SyncDisabled.ToString() };

            string? sessionId = null;
            byte[]? codeProof = null;
            string? sourceDeviceId = null;
            byte[]? sourceSignPublicKey = null;
            string? sourceTlsCertFingerprint = null;
            var totalBytes = 0L;

            await using var snapshotStream = new MemoryStream();

            foreach (var chunk in chunks)
            {
                if (!string.IsNullOrWhiteSpace(chunk.SessionId))
                    sessionId = chunk.SessionId;

                if (chunk.CodeProof.Length > 0)
                    codeProof = chunk.CodeProof.ToByteArray();

                if (!string.IsNullOrWhiteSpace(chunk.SourceDeviceId))
                    sourceDeviceId = chunk.SourceDeviceId;

                if (chunk.SourceSignPub.Length > 0)
                    sourceSignPublicKey = chunk.SourceSignPub.ToByteArray();

                if (!string.IsNullOrWhiteSpace(chunk.SourceTlsCertFingerprint))
                    sourceTlsCertFingerprint = chunk.SourceTlsCertFingerprint;

                var bytes = chunk.SnapshotChunk.ToByteArray();
                totalBytes += bytes.Length;

                if (totalBytes > SyncConstants.MaxDeviceEnrollmentSnapshotBytes)
                {
                    return new CompleteDeviceEnrollmentReply
                    {
                        Ok = false,
                        Error = "The profile data is too large to transfer in one enrollment request.",
                        ErrorCode = DeviceEnrollmentErrorCode.ProfileDataTooLarge.ToString()
                    };
                }

                if (bytes.Length > 0)
                    await snapshotStream.WriteAsync(bytes, ct);
            }

            if (string.IsNullOrWhiteSpace(sessionId) ||
                codeProof is null ||
                string.IsNullOrWhiteSpace(sourceDeviceId) ||
                sourceSignPublicKey is null ||
                string.IsNullOrWhiteSpace(sourceTlsCertFingerprint))
            {
                return new CompleteDeviceEnrollmentReply
                {
                    Ok = false,
                    Error = "The enrollment transfer metadata is incomplete.",
                    ErrorCode = DeviceEnrollmentErrorCode.ProfileDataInvalid.ToString()
                };
            }

            var enrollment = scope.ServiceProvider.GetRequiredService<IDeviceEnrollmentService>();
            var result = await enrollment.CompleteIncomingEnrollmentAsync(
                sessionId,
                codeProof,
                snapshotStream.ToArray(),
                sourceDeviceId,
                sourceSignPublicKey,
                sourceTlsCertFingerprint,
                context.ClientCertificateFingerprint,
                context.RemoteIpAddress,
                ct);

            DeviceEnrollmentTrace.Info($"Incoming streaming CompleteDeviceEnrollment request finished. Ok={result.Ok}, ErrorCode={result.ErrorCode}, Error={result.Error}");

            return new CompleteDeviceEnrollmentReply
            {
                Ok = result.Ok,
                Error = result.Error ?? string.Empty,
                ErrorCode = result.ErrorCode.ToString()
            };
        }
        catch (Exception ex)
        {
            DeviceEnrollmentTrace.Error($"Incoming streaming CompleteDeviceEnrollment request failed: {ex.Message}", ex);
            return new CompleteDeviceEnrollmentReply
            {
                Ok = false,
                Error = ex.Message,
                ErrorCode = DeviceEnrollmentErrorCode.Unknown.ToString()
            };
        }
    }


    private static async Task<Device?> TryFindRemoteDeviceForInvalidAttemptAsync(IServiceProvider services, PeerConnectionContext context, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(context.ClientCertificateFingerprint))
                return null;

            var identity = services.GetRequiredService<IDeviceIdentityService>();
            var devices = services.GetRequiredService<IDeviceRepository>();
            var fingerprint = context.ClientCertificateFingerprint;

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
        PeerConnectionContext context,
        string? claimedDeviceId,
        byte[]? claimedSignPublicKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.ClientCertificateFingerprint))
            throw new SyncProtocolException(SyncProtocolStatusCode.Unauthenticated, "Client certificate is missing.");

        var identity = services.GetRequiredService<IDeviceIdentityService>();
        if (!identity.IsSyncOn)
            throw new SyncProtocolException(SyncProtocolStatusCode.Unavailable, "Local synchronization is disabled.");

        var devices = services.GetRequiredService<IDeviceRepository>();

        var fingerprint = context.ClientCertificateFingerprint;
        if (string.Equals(NormalizeFingerprint(fingerprint), NormalizeFingerprint(identity.FingerprintHex), StringComparison.OrdinalIgnoreCase))
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Local device cannot sync with itself.");

        var remoteDevice = await devices.GetByTlsCertFingerprintWithUserDevicesAsync(fingerprint, ct);
        if (remoteDevice is null)
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device is not trusted.");

        if (!remoteDevice.IsTrusted)
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device is not allowed to sync.");

        if (remoteDevice.IsBlocked)
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device is not allowed to sync.");

        if (!remoteDevice.UserDevices.Any(ud => !ud.IsDeleted && ud.IsSyncEnabled))
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device is not linked to any active user.");

        if (remoteDevice.SignPublicKey.Length == 0)
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device signing key is missing.");

        if (claimedSignPublicKey is not null)
        {
            if (claimedSignPublicKey.Length == 0)
                throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device signing key is missing.");

            if (!remoteDevice.SignPublicKey.SequenceEqual(claimedSignPublicKey))
                throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device signing key does not match.");
        }

        if (claimedDeviceId is not null)
        {
            if (string.IsNullOrWhiteSpace(claimedDeviceId))
                throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device id is missing.");

            if (!string.Equals(claimedDeviceId, BuildDeviceId(remoteDevice.SignPublicKey), StringComparison.OrdinalIgnoreCase))
                throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Remote device id does not match.");
        }

        if (remoteDevice.Id == identity.LocalDeviceId || identity.SignPublicKey.SequenceEqual(remoteDevice.SignPublicKey))
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Local device cannot sync with itself.");

        return remoteDevice;
    }


    private static void ValidateDeltaTransport(NetworkDelta delta, Device remoteDevice, Guid localDeviceId)
    {
        if (string.IsNullOrWhiteSpace(delta.Entity) || delta.Entity.Length > 256)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta entity is invalid.");

        if (delta.Payload.Length == 0 || delta.Payload.Length > SyncConstants.MaxIncomingDeltaPayloadBytes)
            throw new SyncProtocolException(SyncProtocolStatusCode.ResourceExhausted, "Delta payload size is invalid.");

        if (delta.Ts <= 0)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta timestamp is invalid.");

        var maxFutureTs = DateTimeOffset.UtcNow.AddSeconds(SyncConstants.MaxIncomingDeltaFutureSeconds).ToUnixTimeMilliseconds();
        if (delta.Ts > maxFutureTs)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta timestamp is too far in the future.");

        if (delta.SignPub.Length != SyncConstants.SyncDeltaEd25519PublicKeyBytes ||
            delta.Sig.Length != SyncConstants.SyncDeltaEd25519SignatureBytes)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta signature is incomplete.");

        if (!remoteDevice.SignPublicKey.SequenceEqual(delta.SignPub))
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Delta signer is not the connected device.");

        if (!string.Equals(delta.DeviceId, BuildDeviceId(delta.SignPub), StringComparison.OrdinalIgnoreCase))
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Delta device id is invalid.");

        if (delta.EncryptionVersion != SyncConstants.SyncDeltaEncryptionVersion)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta encryption version is invalid.");

        if (!Guid.TryParseExact(delta.RecipientDeviceId, "N", out var recipientDeviceId) &&
            !Guid.TryParse(delta.RecipientDeviceId, out recipientDeviceId))
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta recipient is invalid.");

        if (recipientDeviceId != localDeviceId)
            throw new SyncProtocolException(SyncProtocolStatusCode.PermissionDenied, "Delta recipient is not this device.");

        if (delta.EphemeralPublicKey.Length != SyncConstants.SyncDeltaX25519PublicKeyBytes)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta ephemeral key is invalid.");

        if (delta.Nonce.Length != SyncConstants.SyncDeltaNonceBytes)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta nonce is invalid.");

        if (delta.Tag.Length != SyncConstants.SyncDeltaTagBytes)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta authentication tag is invalid.");

        if (delta.PayloadHash.Length != SyncConstants.SyncDeltaPayloadHashBytes)
            throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Delta payload hash is invalid.");
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


    private static bool IsInvalidIncomingDataStatus(SyncProtocolStatusCode statusCode) =>
        statusCode == SyncProtocolStatusCode.InvalidArgument ||
        statusCode == SyncProtocolStatusCode.PermissionDenied ||
        statusCode == SyncProtocolStatusCode.ResourceExhausted ||
        statusCode == SyncProtocolStatusCode.FailedPrecondition;


    private static string BuildDeviceId(byte[] signPublicKey) =>
        Convert.ToHexString(Hashing.SHA256Hash(signPublicKey));


    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
