using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Sync;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalBackend.Services;

public class GrpcClientService : IGrpcClientService
{
    private readonly IDeviceIdentityService _identity;

    public GrpcClientService(IDeviceIdentityService identity)
    {
        _identity = identity;
    }




    public async Task<bool> SendAsync(string host, int port, string serverFingerprintHex, IEnumerable<NetworkDelta> deltas, CancellationToken ct = default)
    {
        var list = deltas.ToList();
        if (list.Count == 0)
            return true;

        try
        {
            using var handler = CreateHandler(serverFingerprintHex);
            using var channel = GrpcChannel.ForAddress(BuildAddress(host, port), new GrpcChannelOptions
            {
                HttpHandler = handler
            });

            var client = new SyncGrpc.SyncGrpcClient(channel);

            var hello = await client.HelloAsync(new HelloRequest
            {
                DeviceId = _identity.DeviceIdHex,
                SignPub = ByteString.CopyFrom(_identity.SignPublicKey)
            }, cancellationToken: ct);

            if (!hello.Ok)
                return false;

            using var call = client.PushDelta(cancellationToken: ct);

            foreach (var delta in list.OrderBy(d => d.Ts))
                await call.RequestStream.WriteAsync(DeltaMapping.ToProto(PrepareDelta(delta)));

            await call.RequestStream.CompleteAsync();
            var ack = await call.ResponseAsync;

            return ack.LastSyncedTs >= list.Max(x => x.Ts);
        }
        catch (RpcException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
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
    }


    private HttpClientHandler CreateHandler(string serverFingerprintHex)
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(_identity.Certificate);

        handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
        {
            if (cert is null)
                return false;

            var fingerprint = NormalizeFingerprint(_identity.GetFingerprintHex(new X509Certificate2(cert)));
            return string.Equals(fingerprint, NormalizeFingerprint(serverFingerprintHex), StringComparison.OrdinalIgnoreCase);
        };

        return handler;
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


    private static string BuildAddress(string host, int port)
    {
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return $"https://[{host}]:{port}";

        return $"https://{host}:{port}";
    }
}
