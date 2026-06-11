using Google.Protobuf;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Sync.Tcp;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalBackend.Services;

public sealed class TcpSyncClientService : ISyncTransportClientService
{
    private readonly IDeviceIdentityService _identity;

    public TcpSyncClientService(IDeviceIdentityService identity)
    {
        _identity = identity;
    }




    public async Task<bool> SendDeltasAsync(string host, int port, string serverFingerprintHex, IEnumerable<NetworkDelta> deltas, CancellationToken ct = default)
    {
        var list = deltas.ToList();
        if (list.Count == 0)
            return true;

        try
        {
            await using var connection = await ConnectAsync(host, port, serverFingerprintHex, ct);

            await SyncTcpFrameIo.WriteAsync(connection.Stream, SyncTcpMessageType.HelloRequest, new HelloRequest
            {
                DeviceId = _identity.DeviceIdHex,
                SignPub = ByteString.CopyFrom(_identity.SignPublicKey)
            }, ct);

            var helloFrame = await ReadRequiredAsync(connection.Stream, SyncTcpMessageType.HelloReply, ct);
            var hello = helloFrame.Parse(HelloReply.Parser);
            if (!hello.Ok)
                return false;

            await SyncTcpFrameIo.WriteAsync(connection.Stream, SyncTcpMessageType.PushDeltaStart, ct);

            foreach (var delta in list.OrderBy(d => d.Ts))
                await SyncTcpFrameIo.WriteAsync(connection.Stream, SyncTcpMessageType.DeltaChunk, DeltaMapping.ToProto(PrepareDelta(delta)), ct);

            await SyncTcpFrameIo.WriteAsync(connection.Stream, SyncTcpMessageType.PushDeltaEnd, ct);

            var ackFrame = await ReadRequiredAsync(connection.Stream, SyncTcpMessageType.Ack, ct);
            var ack = ackFrame.Parse(Ack.Parser);

            return ack.LastSyncedTs >= list.Max(x => x.Ts);
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (AuthenticationException)
        {
            return false;
        }
    }


    public async Task<GetDeviceEnrollmentInfoReply> GetDeviceEnrollmentInfoAsync(string host, int port, string serverFingerprintHex, GetDeviceEnrollmentInfoRequest request, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(host, port, serverFingerprintHex, ct);

        await SyncTcpFrameIo.WriteAsync(connection.Stream, SyncTcpMessageType.GetDeviceEnrollmentInfoRequest, request, ct);
        var frame = await ReadRequiredAsync(connection.Stream, SyncTcpMessageType.GetDeviceEnrollmentInfoReply, ct);
        return frame.Parse(GetDeviceEnrollmentInfoReply.Parser);
    }


    public async Task<CompleteDeviceEnrollmentReply> CompleteDeviceEnrollmentStreamAsync(string host, int port, string serverFingerprintHex, IAsyncEnumerable<CompleteDeviceEnrollmentChunk> chunks, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(host, port, serverFingerprintHex, ct);

        await SyncTcpFrameIo.WriteAsync(connection.Stream, SyncTcpMessageType.CompleteDeviceEnrollmentStart, ct);

        await foreach (var chunk in chunks.WithCancellation(ct))
            await SyncTcpFrameIo.WriteAsync(connection.Stream, SyncTcpMessageType.CompleteDeviceEnrollmentChunk, chunk, ct);

        await SyncTcpFrameIo.WriteAsync(connection.Stream, SyncTcpMessageType.CompleteDeviceEnrollmentEnd, ct);

        var frame = await ReadRequiredAsync(connection.Stream, SyncTcpMessageType.CompleteDeviceEnrollmentReply, ct);
        return frame.Parse(CompleteDeviceEnrollmentReply.Parser);
    }


    private async Task<TcpSyncClientConnection> ConnectAsync(string host, int port, string serverFingerprintHex, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.DeviceEnrollmentConnectTimeoutSeconds));

        var client = new System.Net.Sockets.TcpClient();
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            var stream = new SslStream(client.GetStream(), false);
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificates = new X509CertificateCollection { _identity.Certificate },
                LocalCertificateSelectionCallback = (_, _, _, _, _) => _identity.Certificate,
                RemoteCertificateValidationCallback = (_, cert, _, _) => ValidatePinnedServerCertificate(cert, serverFingerprintHex),
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token);

            return new TcpSyncClientConnection(client, stream);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }


    private async Task<SyncTcpFrame> ReadRequiredAsync(Stream stream, SyncTcpMessageType expectedType, CancellationToken ct)
    {
        var frame = await SyncTcpFrameIo.ReadAsync(stream, ct) ?? throw new EndOfStreamException();
        if (frame.Type == SyncTcpMessageType.Error)
        {
            var error = frame.Parse(SyncError.Parser);
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error.Message) ? error.Code : error.Message);
        }

        if (frame.Type != expectedType)
            throw new InvalidDataException($"Unexpected sync TCP frame type. Expected={expectedType}, Actual={frame.Type}.");

        return frame;
    }


    private bool ValidatePinnedServerCertificate(X509Certificate? cert, string serverFingerprintHex)
    {
        if (cert is null)
            return false;

        var fingerprint = NormalizeFingerprint(_identity.GetFingerprintHex(new X509Certificate2(cert)));
        var expected = NormalizeFingerprint(serverFingerprintHex);
        if (expected.Length == 0)
            return false;

        return expected.Length < 64
            ? fingerprint.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
            : string.Equals(fingerprint, expected, StringComparison.OrdinalIgnoreCase);
    }


    private NetworkDelta PrepareDelta(NetworkDelta delta)
    {
        if (!string.Equals(delta.DeviceId, _identity.DeviceIdHex, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Delta source device id is invalid.");

        if (!delta.SignPub.SequenceEqual(_identity.SignPublicKey))
            throw new InvalidDataException("Delta signer is invalid.");

        if (string.IsNullOrWhiteSpace(delta.RecipientDeviceId))
            throw new InvalidDataException("Delta recipient is missing.");

        if (delta.EncryptionVersion != SyncConstants.SyncDeltaEncryptionVersion ||
            delta.Payload.Length == 0 ||
            delta.Payload.Length > SyncConstants.MaxIncomingDeltaPayloadBytes ||
            delta.EphemeralPublicKey.Length != SyncConstants.SyncDeltaX25519PublicKeyBytes ||
            delta.Nonce.Length != SyncConstants.SyncDeltaNonceBytes ||
            delta.Tag.Length != SyncConstants.SyncDeltaTagBytes ||
            delta.PayloadHash.Length != SyncConstants.SyncDeltaPayloadHashBytes)
            throw new InvalidDataException("Delta encryption envelope is incomplete.");

        if (delta.Sig.Length == 0)
            NetDeltaSigner.FillSignature(delta, _identity);
        else if (!NetDeltaSigner.VerifySignature(delta))
            throw new InvalidDataException("Delta signature is invalid.");

        return delta;
    }


    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();


    private sealed class TcpSyncClientConnection : IAsyncDisposable
    {
        private readonly System.Net.Sockets.TcpClient _client;

        public TcpSyncClientConnection(System.Net.Sockets.TcpClient client, SslStream stream)
        {
            _client = client;
            Stream = stream;
        }

        public SslStream Stream { get; }

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
            _client.Dispose();
        }
    }
}
