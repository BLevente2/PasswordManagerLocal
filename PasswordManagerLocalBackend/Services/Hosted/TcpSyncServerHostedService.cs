using Google.Protobuf;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Services.Tcp;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Sync.Tcp;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class TcpSyncServerHostedService : ISyncControlledHostedService
{
    private readonly IDeviceIdentityService _identity;
    private readonly SyncPeerProtocolHandler _handler;
    private readonly SemaphoreSlim _connectionSlots = new(SyncConstants.MaxConcurrentSyncConnections, SyncConstants.MaxConcurrentSyncConnections);
    private readonly ConcurrentDictionary<string, int> _connectionsByRemoteIp = new(StringComparer.OrdinalIgnoreCase);
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

            if (!TryBeginConnection(client, out var remoteIp))
            {
                client.Dispose();
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, remoteIp, ct), CancellationToken.None);
        }
    }


    private async Task HandleClientAsync(TcpClient client, string remoteIp, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                client.ReceiveTimeout = SyncConstants.SyncTcpIdleTimeoutSeconds * 1000;
                client.SendTimeout = SyncConstants.SyncTcpWriteTimeoutSeconds * 1000;

                await using var ssl = new SslStream(client.GetStream(), false);

                using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.SyncTcpHandshakeTimeoutSeconds));

                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _identity.Certificate,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null
                }, handshakeTimeout.Token);

                var context = new PeerConnectionContext
                {
                    RemoteIpAddress = remoteIp,
                    ClientCertificateFingerprint = BuildRemoteCertificateFingerprint(ssl.RemoteCertificate)
                };

                await HandleFramesAsync(ssl, context, ct);
            }
        }
        catch
        {
        }
        finally
        {
            EndConnection(remoteIp);
        }
    }


    private async Task HandleFramesAsync(Stream stream, PeerConnectionContext context, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, ct);
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
        await WriteFrameAsync(stream, SyncTcpMessageType.HelloReply, reply, ct);
    }


    private async Task HandlePushDeltaAsync(Stream stream, PeerConnectionContext context, CancellationToken ct)
    {
        var ack = await _handler.PushDeltaAsync(ReadDeltaChunksAsync(stream, ct), context, ct);
        await WriteFrameAsync(stream, SyncTcpMessageType.Ack, ack, ct);
    }


    private async IAsyncEnumerable<DeltaChunk> ReadDeltaChunksAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, ct) ?? throw new EndOfStreamException();
            if (frame.Type == SyncTcpMessageType.PushDeltaEnd)
                yield break;

            if (frame.Type != SyncTcpMessageType.DeltaChunk)
                throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Expected delta chunk frame.");

            yield return frame.Parse(DeltaChunk.Parser);
        }
    }


    private async Task HandleGetDeviceEnrollmentInfoAsync(Stream stream, SyncTcpFrame frame, CancellationToken ct)
    {
        var request = frame.Parse(GetDeviceEnrollmentInfoRequest.Parser);
        var reply = await _handler.GetDeviceEnrollmentInfoAsync(request, ct);
        await WriteFrameAsync(stream, SyncTcpMessageType.GetDeviceEnrollmentInfoReply, reply, ct);
    }


    private async Task HandleCompleteDeviceEnrollmentAsync(Stream stream, PeerConnectionContext context, CancellationToken ct)
    {
        var reply = await _handler.CompleteDeviceEnrollmentStreamAsync(ReadEnrollmentChunksAsync(stream, ct), context, ct);
        await WriteFrameAsync(stream, SyncTcpMessageType.CompleteDeviceEnrollmentReply, reply, ct);
    }


    private async IAsyncEnumerable<CompleteDeviceEnrollmentChunk> ReadEnrollmentChunksAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        var totalBytes = 0L;

        while (!ct.IsCancellationRequested)
        {
            var frame = await ReadFrameAsync(stream, ct) ?? throw new EndOfStreamException();
            if (frame.Type == SyncTcpMessageType.CompleteDeviceEnrollmentEnd)
                yield break;

            if (frame.Type != SyncTcpMessageType.CompleteDeviceEnrollmentChunk)
                throw new SyncProtocolException(SyncProtocolStatusCode.InvalidArgument, "Expected enrollment chunk frame.");

            var chunk = frame.Parse(CompleteDeviceEnrollmentChunk.Parser);
            totalBytes += chunk.SnapshotChunk.Length;
            if (totalBytes > SyncConstants.MaxDeviceEnrollmentSnapshotBytes)
                throw new SyncProtocolException(SyncProtocolStatusCode.ResourceExhausted, "The profile data is too large to transfer in one enrollment request.");

            yield return chunk;
        }
    }


    private async Task WriteErrorAsync(Stream stream, SyncProtocolStatusCode statusCode, string message, CancellationToken ct)
    {
        await WriteFrameAsync(stream, SyncTcpMessageType.Error, new SyncError
        {
            Code = statusCode.ToString(),
            Message = message
        }, ct);
    }


    private async Task<SyncTcpFrame?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.SyncTcpIdleTimeoutSeconds));
        return await SyncTcpFrameIo.ReadAsync(stream, timeout.Token);
    }


    private static async Task WriteFrameAsync(Stream stream, SyncTcpMessageType type, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.SyncTcpWriteTimeoutSeconds));
        await SyncTcpFrameIo.WriteAsync(stream, type, timeout.Token);
    }


    private static async Task WriteFrameAsync(Stream stream, SyncTcpMessageType type, IMessage message, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.SyncTcpWriteTimeoutSeconds));
        await SyncTcpFrameIo.WriteAsync(stream, type, message, timeout.Token);
    }


    private bool TryBeginConnection(TcpClient client, out string remoteIp)
    {
        remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";

        if (!_connectionSlots.Wait(0))
            return false;

        var count = _connectionsByRemoteIp.AddOrUpdate(remoteIp, 1, (_, current) => current + 1);
        if (count <= SyncConstants.MaxSyncConnectionsPerRemoteIp)
            return true;

        EndConnection(remoteIp);
        return false;
    }


    private void EndConnection(string remoteIp)
    {
        _connectionSlots.Release();

        _connectionsByRemoteIp.AddOrUpdate(
            remoteIp,
            0,
            (_, current) => current <= 1 ? 0 : current - 1);

        if (_connectionsByRemoteIp.TryGetValue(remoteIp, out var count) && count <= 0)
            _connectionsByRemoteIp.TryRemove(remoteIp, out _);
    }


    private string? BuildRemoteCertificateFingerprint(X509Certificate? certificate)
    {
        if (certificate is null)
            return null;

        return _identity.GetFingerprintHex(new X509Certificate2(certificate));
    }
}
