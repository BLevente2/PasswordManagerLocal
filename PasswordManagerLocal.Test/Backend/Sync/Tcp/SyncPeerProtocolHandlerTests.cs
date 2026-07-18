using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Tcp;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Test.Fakes;

namespace PasswordManagerLocal.Test.Backend.Sync.Tcp;

[TestClass]
public sealed class SyncPeerProtocolHandlerTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public async Task PushDelta_WhenProfileKeyIsUnavailable_DoesNotAcknowledgeOrRememberDeltaAsApplied()
    {
        var localIdentity = new FakeDeviceIdentityService
        {
            IsSyncOn = true,
            LocalDeviceId = Guid.NewGuid(),
            SignPublicKey = Enumerable.Repeat((byte)0x11, SyncConstants.SyncDeltaEd25519PublicKeyBytes).ToArray(),
            FingerprintHex = "LOCAL-FINGERPRINT"
        };
        var remoteSignPublicKey = Enumerable.Range(1, SyncConstants.SyncDeltaEd25519PublicKeyBytes)
            .Select(value => (byte)value)
            .ToArray();
        var remoteDevice = new Device
        {
            Id = Guid.NewGuid(),
            SignPublicKey = remoteSignPublicKey,
            TlsCertFingerprint = "REMOTE-FINGERPRINT",
            IsTrusted = true,
            IsBlocked = false
        };
        var devices = new FakeDeviceRepository();
        devices.Seed(remoteDevice);
        var applier = new DeferredIncomingDeltaApplierService();
        var deviceSecurity = new RecordingDeviceSecurityService();

        var services = new ServiceCollection();
        services.AddSingleton<IDeviceIdentityService>(localIdentity);
        services.AddSingleton<IDeviceRepository>(devices);
        services.AddSingleton<IIncomingDeltaApplierService>(applier);
        services.AddSingleton<IDeviceSecurityService>(deviceSecurity);
        using var provider = services.BuildServiceProvider();
        var handler = new SyncPeerProtocolHandler(provider);
        var context = new PeerConnectionContext
        {
            ClientCertificateFingerprint = remoteDevice.TlsCertFingerprint,
            RemoteDatabaseVersion = DatabaseConstants.CurrentDbVersion,
            SyncHelloAccepted = true
        };
        var delta = CreateTransportValidDelta(localIdentity.LocalDeviceId, remoteSignPublicKey);

        await ExpectUnavailableAsync(handler, context, delta);
        await ExpectUnavailableAsync(handler, context, delta);

        Assert.AreEqual(2, applier.ApplyCalls, "A deferred delta must be attempted again instead of being treated as a recent successful replay.");
        Assert.AreEqual(0, deviceSecurity.ResetCalls, "The remote device must not be marked as successfully synchronized when the delta was deferred.");
        Assert.AreEqual(0, deviceSecurity.RecordInvalidCalls, "A temporarily unavailable profile key is not invalid or hostile incoming data.");
    }

    private static NetworkDelta CreateTransportValidDelta(Guid recipientDeviceId, byte[] remoteSignPublicKey) =>
        new()
        {
            Entity = "User",
            Payload = new byte[] { 1 },
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DeviceId = Convert.ToHexString(Hashing.SHA256Hash(remoteSignPublicKey)),
            SignPub = remoteSignPublicKey.ToArray(),
            Sig = Enumerable.Repeat((byte)0x22, SyncConstants.SyncDeltaEd25519SignatureBytes).ToArray(),
            RecipientDeviceId = recipientDeviceId.ToString("N"),
            EncryptionVersion = SyncConstants.SyncDeltaEncryptionVersion,
            EphemeralPublicKey = Enumerable.Repeat((byte)0x33, SyncConstants.SyncDeltaX25519PublicKeyBytes).ToArray(),
            Nonce = Enumerable.Repeat((byte)0x44, SyncConstants.SyncDeltaNonceBytes).ToArray(),
            Tag = Enumerable.Repeat((byte)0x55, SyncConstants.SyncDeltaTagBytes).ToArray(),
            PayloadHash = Enumerable.Repeat((byte)0x66, SyncConstants.SyncDeltaPayloadHashBytes).ToArray()
        };

    private static async Task ExpectUnavailableAsync(
        SyncPeerProtocolHandler handler,
        PeerConnectionContext context,
        NetworkDelta delta)
    {
        try
        {
            await handler.PushDeltaAsync(SingleDeltaAsync(delta), context, CancellationToken.None);
            Assert.Fail("Expected the deferred delta to terminate the call without returning an acknowledgement.");
        }
        catch (SyncProtocolException ex)
        {
            Assert.AreEqual(SyncProtocolStatusCode.Unavailable, ex.StatusCode);
        }
    }

    private static async IAsyncEnumerable<DeltaChunk> SingleDeltaAsync(NetworkDelta delta)
    {
        yield return DeltaMapping.ToProto(delta);
        await Task.CompletedTask;
    }

    private sealed class DeferredIncomingDeltaApplierService : IIncomingDeltaApplierService
    {
        public int ApplyCalls { get; private set; }

        public Task<long> ApplyAsync(NetworkDelta delta, CancellationToken ct = default)
        {
            ApplyCalls++;
            throw new SyncDeltaDeferredException(Guid.NewGuid());
        }
    }

    private sealed class RecordingDeviceSecurityService : IDeviceSecurityService
    {
        public int RecordInvalidCalls { get; private set; }
        public int ResetCalls { get; private set; }

        public Task RecordInvalidIncomingSyncAsync(Device device, string reason, CancellationToken ct = default)
        {
            RecordInvalidCalls++;
            return Task.CompletedTask;
        }

        public Task ResetInvalidIncomingSyncAsync(Device device, CancellationToken ct = default)
        {
            ResetCalls++;
            return Task.CompletedTask;
        }
    }
}
