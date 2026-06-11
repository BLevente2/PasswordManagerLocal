using Google.Protobuf;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Services.Tcp;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Sync.Tcp;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class TcpSyncServerHostedService : ISyncControlledHostedService
{
    private readonly IDeviceIdentityService _identity;
    private readonly SyncPeerProtocolHandler _handler;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public TcpSyncServerHostedService(IDeviceIdentityService identity, SyncPeerProtocolHandler handler)
    {
        _identity = identity;
        _handler = handler;
    }




    public int StartOrder => 20;




    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_identity.IsSyncOn)
            return Task.CompletedTask;

        if (_listener is not null)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, SyncConstants.SyncPort);
        _listener.Start();

        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }


    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _listener?.Stop();

        if (_acceptLoopTask is not null)
            await Task.WhenAny(_acceptLoopTask, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));

        _listener = null;
        _acceptLoopTask = null;

        _cts?.Dispose();
        _cts = null;
    }


    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
        }
    }


    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            await using var ssl = new SslStream(client.GetStream(), false);

            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _identity.Certificate,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null
                }, ct);

                var context = new PeerConnectionContext
                {
                    RemoteIpAddress = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString(),
                    ClientCertificateFingerprint = BuildRemoteCertificateFingerprint(ssl.RemoteCertificate)
                };

                await HandleFramesAsync(ssl, context, ct);
            }
            catch
            {
            }
        }
    }


    private async Task HandleFramesAsync(Stream stream, PeerConnectionContext context, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await SyncTcpFrameIo.ReadAsync(stream, ct);
            if (frame is null)
                return;

            try
            {
                switch (frame.Type)
                {
                    case SyncTcpMessageType.HelloRequest:
                        await HandleHelloAsync(stream, frame, context, ct);
                        break;

                    case SyncTcpMessageType.PushDeltaStart:
                        await HandlePushDeltaAsync(stream, context, ct);
                        return;

                    case SyncTcpMessageType.GetDeviceEnrollmentInfoRequest:
                        await HandleGetDeviceEnrollmentInfoAsync(stream, frame, ct);
                        return;

                    case SyncTcpMessageType.CompleteDeviceEnrollmentStart:
                        await HandleCompleteDeviceEnrollmentAsync(stream, context, ct);
                        return;

                    default:
                        await WriteErrorAsync(stream, SyncProtocolStatusCode.InvalidArgument, "Unsupported sync TCP frame type.", ct);
                        return;
                }
            }
            catch (SyncProtocolException ex)
            {
                await WriteErrorAsync(stream, ex.StatusCode, ex.Message, ct);
                return;
            }
            catch (InvalidDataException ex)
            {
                await WriteErrorAsync(stream, SyncProtocolStatusCode.InvalidArgument, ex.Message, ct);
                return;
            }
        }
    }


    private async Task HandleHelloAsync(Stream stream, SyncTcpFrame frame, PeerConnectionContext context, CancellationToken ct)
    {
        var request = frame.Parse(HelloRequest.Parser);
        var reply = await _handler.HelloAsync(request, context, ct);
        await SyncTcpFrameIo.WriteAsync(stream, SyncTcpMessageType.HelloReply, reply, ct);
    }


    private async Task HandlePushDeltaAsync(Stream stream, PeerConnectionContext context, CancellationToken ct)
    {
        var chunks = new List<DeltaChunk>();

        while (!ct.IsCancellationRequested)
        {
            var frame = await SyncTcpFrameIo.ReadAsync(stream, ct) ?? throw new EndOfStreamException();
            if (frame.Type == SyncTcpMessageType.PushDeltaEnd)
                break;

            if (frame.Type != SyncTcpMessageType.DeltaChunk)
                throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Expected delta chunk frame.");

            chunks.Add(frame.Parse(DeltaChunk.Parser));
            if (chunks.Count > SyncConstants.MaxIncomingDeltaCountPerCall)
                throw new SyncProtocolException(SyncProtocolStatusCode.ResourceExhausted, "Too many deltas in one sync call.");
        }

        var ack = await _handler.PushDeltaAsync(chunks, context, ct);
        await SyncTcpFrameIo.WriteAsync(stream, SyncTcpMessageType.Ack, ack, ct);
    }


    private async Task HandleGetDeviceEnrollmentInfoAsync(Stream stream, SyncTcpFrame frame, CancellationToken ct)
    {
        var request = frame.Parse(GetDeviceEnrollmentInfoRequest.Parser);
        var reply = await _handler.GetDeviceEnrollmentInfoAsync(request, ct);
        await SyncTcpFrameIo.WriteAsync(stream, SyncTcpMessageType.GetDeviceEnrollmentInfoReply, reply, ct);
    }


    private async Task HandleCompleteDeviceEnrollmentAsync(Stream stream, PeerConnectionContext context, CancellationToken ct)
    {
        var chunks = new List<CompleteDeviceEnrollmentChunk>();
        var totalBytes = 0L;

        while (!ct.IsCancellationRequested)
        {
            var frame = await SyncTcpFrameIo.ReadAsync(stream, ct) ?? throw new EndOfStreamException();
            if (frame.Type == SyncTcpMessageType.CompleteDeviceEnrollmentEnd)
                break;

            if (frame.Type != SyncTcpMessageType.CompleteDeviceEnrollmentChunk)
                throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Expected enrollment chunk frame.");

            var chunk = frame.Parse(CompleteDeviceEnrollmentChunk.Parser);
            totalBytes += chunk.SnapshotChunk.Length;
            if (totalBytes > SyncConstants.MaxDeviceEnrollmentSnapshotBytes)
                throw new SyncProtocolException(SyncProtocolStatusCode.ResourceExhausted, "The profile data is too large to transfer in one enrollment request.");

            chunks.Add(chunk);
        }

        var reply = await _handler.CompleteDeviceEnrollmentStreamAsync(chunks, context, ct);
        await SyncTcpFrameIo.WriteAsync(stream, SyncTcpMessageType.CompleteDeviceEnrollmentReply, reply, ct);
    }


    private async Task WriteErrorAsync(Stream stream, SyncProtocolStatusCode statusCode, string message, CancellationToken ct)
    {
        await SyncTcpFrameIo.WriteAsync(stream, SyncTcpMessageType.Error, new SyncError
        {
            Code = statusCode.ToString(),
            Message = message
        }, ct);
    }


    private string? BuildRemoteCertificateFingerprint(X509Certificate? certificate)
    {
        if (certificate is null)
            return null;

        return _identity.GetFingerprintHex(new X509Certificate2(certificate));
    }
}
