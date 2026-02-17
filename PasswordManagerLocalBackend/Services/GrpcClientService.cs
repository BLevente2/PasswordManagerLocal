using Grpc.Net.Client;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Sync;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;

namespace PasswordManagerLocalBackend.Services;

public class GrpcClientService : IGrpcClientService
{
    private readonly IDeviceIdentityService _identiy;

    public GrpcClientService(IDeviceIdentityService identiy)
    {
        _identiy = identiy;
    }


    public async Task<bool> SendAsync(string host, int port, string serverFingerprintHex, IEnumerable<NetworkDelta> deltas, CancellationToken ct = default)
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(_identiy.Certificate);

        handler.ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
        {
            if (cert == null)
                return false;
            var fp = _identiy.GetFingerprintHex(new X509Certificate2(cert));
            return string.Equals(fp, serverFingerprintHex, StringComparison.OrdinalIgnoreCase);
        };

        using var http = new HttpClient(handler);
        var addr = $"https://{host}:{port}";
        var channel = GrpcChannel.ForAddress(addr, new GrpcChannelOptions { HttpClient = http });

        var client = new SyncGrpc.SyncGrpcClient(channel);

        var hello = await client.HelloAsync(new HelloRequest
        {
            SignPub = ByteString.CopyFrom(_identiy.SignPublicKey)
        }, cancellationToken: ct);

        if (!hello.Ok)
            return false;

        var ns = await client.NeedSyncAsync(new NeedSyncRequest
        {
            SinceTs = 0
        }, cancellationToken: ct);

        var since = ns.LatestTs;
        var list = deltas
            .Where(d => d.Ts > since)
            .OrderBy(d => d.Ts)
            .ToList();

        if (list.Count == 0) return true;

        using var call = client.PushDelta(cancellationToken: ct);
        await foreach (var chunk in ToChunksAsync(list, ct))
            await call.RequestStream.WriteAsync(chunk);

        await call.RequestStream.CompleteAsync();
        var ack = await call.ResponseAsync;
        return ack.LastSyncedTs >= list.Last().Ts;
    }


    private async IAsyncEnumerable<DeltaChunk> ToChunksAsync(IEnumerable<NetworkDelta> deltas, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var d in deltas)
        {
            var dto = new NetworkDelta
            {
                Entity = d.Entity,
                Payload = d.Payload ?? Array.Empty<byte>(),
                Ts = d.Ts,
                DeviceId = _identiy.DeviceIdHex,
                SignPub = _identiy.SignPublicKey
            };

            NetDeltaSigner.FillSignature(dto, _identiy);
            yield return DeltaMapping.ToProto(dto);
            await Task.Yield();
        }
    }
}