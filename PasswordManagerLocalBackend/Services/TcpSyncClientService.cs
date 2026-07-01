using Google.Protobuf;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Sync.Tcp;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using PasswordManagerLocalBackend.Utils;

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

        if (list.Count > SyncConstants.MaxIncomingDeltaCountPerCall || list.Sum(delta => (long)delta.Payload.Length) > SyncConstants.MaxIncomingDeltaTotalBytesPerCall)
            return false;

        try
        {
            await using var connection = await ConnectAsync(host, port, serverFingerprintHex, ct);

            await WriteFrameAsync(connection.Stream, SyncTcpMessageType.HelloRequest, new HelloRequest
            {
                DeviceId = _identity.DeviceIdHex,
                SignPub = ByteString.CopyFrom(_identity.SignPublicKey),
                DatabaseVersion = DatabaseConstants.CurrentDbVersion
            }, ct);

            var helloFrame = await ReadRequiredAsync(connection.Stream, SyncTcpMessageType.HelloReply, ct);
            var hello = helloFrame.Parse(HelloReply.Parser);
            if (!hello.Ok)
                return false;

            await WriteFrameAsync(connection.Stream, SyncTcpMessageType.PushDeltaStart, ct);

            foreach (var delta in list.OrderBy(d => d.Ts))
                await WriteFrameAsync(connection.Stream, SyncTcpMessageType.DeltaChunk, DeltaMapping.ToProto(PrepareDelta(delta)), ct);

            await WriteFrameAsync(connection.Stream, SyncTcpMessageType.PushDeltaEnd, ct);

            var ackFrame = await ReadRequiredAsync(connection.Stream, SyncTcpMessageType.Ack, ct);
            var ack = ackFrame.Parse(Ack.Parser);

            return ack.LastSyncedTs >= list.Max(x => x.Ts);
        }
        catch (System.Net.Sockets.SocketException)
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
        catch (AuthenticationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }


    public async Task<GetDeviceEnrollmentInfoReply> GetDeviceEnrollmentInfoAsync(string host, int port, string serverFingerprintHex, GetDeviceEnrollmentInfoRequest request, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(host, port, serverFingerprintHex, ct);

        await WriteFrameAsync(connection.Stream, SyncTcpMessageType.GetDeviceEnrollmentInfoRequest, request, ct);
        var frame = await ReadRequiredAsync(connection.Stream, SyncTcpMessageType.GetDeviceEnrollmentInfoReply, ct);
        return frame.Parse(GetDeviceEnrollmentInfoReply.Parser);
    }


    public async Task<CompleteDeviceEnrollmentReply> CompleteDeviceEnrollmentStreamAsync(string host, int port, string serverFingerprintHex, IAsyncEnumerable<CompleteDeviceEnrollmentChunk> chunks, CancellationToken ct = default)
    {
        await using var connection = await ConnectAsync(host, port, serverFingerprintHex, ct);

        await WriteFrameAsync(connection.Stream, SyncTcpMessageType.CompleteDeviceEnrollmentStart, new CompleteDeviceEnrollmentStartRequest
        {
            SourceDatabaseVersion = DatabaseConstants.CurrentDbVersion
        }, ct);

        await foreach (var chunk in chunks.WithCancellation(ct))
            await WriteFrameAsync(connection.Stream, SyncTcpMessageType.CompleteDeviceEnrollmentChunk, chunk, ct);

        await WriteFrameAsync(connection.Stream, SyncTcpMessageType.CompleteDeviceEnrollmentEnd, ct);

        var frame = await ReadRequiredAsync(connection.Stream, SyncTcpMessageType.CompleteDeviceEnrollmentReply, ct, SyncConstants.DeviceEnrollmentTransferTimeoutSeconds);
        return frame.Parse(CompleteDeviceEnrollmentReply.Parser);
    }


    private async Task<TcpSyncClientConnection> ConnectAsync(string host, int port, string serverFingerprintHex, CancellationToken ct)
    {
        var preferredSourceAddress = FindPreferredSourceAddress(host);
        var client = await ConnectTcpAsync(host, port, preferredSourceAddress, ct);

        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = SyncConstants.SyncTcpIdleTimeoutSeconds * 1000;
            client.SendTimeout = SyncConstants.SyncTcpWriteTimeoutSeconds * 1000;

            var localEndpoint = client.Client.LocalEndPoint?.ToString() ?? "unknown";
            var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? $"{host}:{port}";
            PasswordManagerLocalBackend.Utils.DeviceEnrollmentTrace.Info($"TCP connection established. Local={localEndpoint}, Remote={remoteEndpoint}. Starting TLS authentication.");

            var stream = new SslStream(client.GetStream(), false);
            using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.SyncTcpHandshakeTimeoutSeconds));

            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificates = new X509CertificateCollection { _identity.Certificate },
                LocalCertificateSelectionCallback = (_, _, _, _, _) => _identity.Certificate,
                RemoteCertificateValidationCallback = (_, cert, _, _) => ValidatePinnedServerCertificate(cert, serverFingerprintHex),
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, handshakeTimeout.Token);

            var serverFingerprint = stream.RemoteCertificate is null
                ? string.Empty
                : _identity.GetFingerprintHex(new X509Certificate2(stream.RemoteCertificate));

            PasswordManagerLocalBackend.Utils.DeviceEnrollmentTrace.Info($"TLS authentication completed. Local={localEndpoint}, Remote={remoteEndpoint}, ServerFingerprintPrefix={FingerprintUtil.Normalize(serverFingerprint)[..Math.Min(16, FingerprintUtil.Normalize(serverFingerprint).Length)]}.");
            return new TcpSyncClientConnection(client, stream);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }


    private async Task<TcpClient> ConnectTcpAsync(string host, int port, IPAddress? preferredSourceAddress, CancellationToken ct)
    {
        var attempts = preferredSourceAddress is null
            ? new IPAddress?[] { null }
            : new IPAddress?[] { preferredSourceAddress, null };

        var errors = new List<Exception>();
        var perAttemptTimeout = TimeSpan.FromSeconds(Math.Max(2, SyncConstants.DeviceEnrollmentConnectTimeoutSeconds / attempts.Length));

        foreach (var sourceAddress in attempts)
        {
            ct.ThrowIfCancellationRequested();

            var client = CreateTcpClient(host, sourceAddress);
            try
            {
                if (sourceAddress is not null)
                    client.Client.Bind(new IPEndPoint(sourceAddress, 0));

                var sourceText = sourceAddress?.ToString() ?? "OS-selected";
                PasswordManagerLocalBackend.Utils.DeviceEnrollmentTrace.Info($"TCP connection attempt started. Source={sourceText}, Target={host}:{port}.");

                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectTimeout.CancelAfter(perAttemptTimeout);
                await client.ConnectAsync(host, port, connectTimeout.Token);
                return client;
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                errors.Add(ex);
                PasswordManagerLocalBackend.Utils.DeviceEnrollmentTrace.Error($"TCP connection attempt timed out. Source={sourceAddress?.ToString() ?? "OS-selected"}, Target={host}:{port}.", ex);
                client.Dispose();
            }
            catch (Exception ex) when (ex is SocketException or IOException or ArgumentException or InvalidOperationException)
            {
                errors.Add(ex);
                PasswordManagerLocalBackend.Utils.DeviceEnrollmentTrace.Error($"TCP connection attempt failed. Source={sourceAddress?.ToString() ?? "OS-selected"}, Target={host}:{port}: {ex.Message}", ex);
                client.Dispose();
            }
        }

        throw new IOException(
            $"No TCP route could connect to {host}:{port}. Tried source addresses: {string.Join(", ", attempts.Select(address => address?.ToString() ?? "OS-selected"))}.",
            errors.LastOrDefault());
    }


    private TcpClient CreateTcpClient(string host, IPAddress? sourceAddress)
    {
        if (sourceAddress is not null)
            return new TcpClient(sourceAddress.AddressFamily);

        if (IPAddress.TryParse(host, out var remoteAddress))
            return new TcpClient(remoteAddress.AddressFamily);

        return new TcpClient();
    }


    private IPAddress? FindPreferredSourceAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var remoteAddress) || remoteAddress.AddressFamily != AddressFamily.InterNetwork)
            return null;

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(LocalNetworkInterfaceUtil.IsOperationalForLocalNetwork)
                .Where(networkInterface => networkInterface.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .SelectMany(networkInterface =>
                {
                    var properties = networkInterface.GetIPProperties();
                    var hasGateway = properties.GatewayAddresses.Any(gateway => IsUsableIpv4(gateway.Address));
                    var isVirtual = IsVirtualAdapter(networkInterface);

                    return properties.UnicastAddresses
                        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && address.IPv4Mask is not null)
                        .Where(address => IsUsableIpv4(address.Address))
                        .Select(address => new
                        {
                            Address = address.Address,
                            SameSubnet = IsInSameIpv4Subnet(remoteAddress, address.Address, address.IPv4Mask!),
                            Priority = (isVirtual ? 0 : 10000) +
                                       (hasGateway ? 3000 : 0) +
                                       GetInterfacePriority(networkInterface.NetworkInterfaceType)
                        });
                })
                .Where(candidate => candidate.SameSubnet)
                .OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Address.ToString(), StringComparer.Ordinal)
                .Select(candidate => candidate.Address)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }


    private bool IsInSameIpv4Subnet(IPAddress remoteAddress, IPAddress localAddress, IPAddress mask)
    {
        var remoteBytes = remoteAddress.GetAddressBytes();
        var localBytes = localAddress.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        if (remoteBytes.Length != 4 || localBytes.Length != 4 || maskBytes.Length != 4)
            return false;

        for (var index = 0; index < 4; index++)
        {
            if ((remoteBytes[index] & maskBytes[index]) != (localBytes[index] & maskBytes[index]))
                return false;
        }

        return true;
    }


    private bool IsUsableIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && !(bytes[0] == 169 && bytes[1] == 254);
    }


    private bool IsVirtualAdapter(NetworkInterface networkInterface)
    {
        var text = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();
        return text.Contains("virtual") ||
               text.Contains("vethernet") ||
               text.Contains("hyper-v") ||
               text.Contains("vmware") ||
               text.Contains("virtualbox") ||
               text.Contains("wsl") ||
               text.Contains("docker") ||
               text.Contains("vpn") ||
               text.Contains("tap") ||
               text.Contains("tunnel");
    }


    private int GetInterfacePriority(NetworkInterfaceType interfaceType) =>
        interfaceType switch
        {
            NetworkInterfaceType.Ethernet => 2500,
            NetworkInterfaceType.GigabitEthernet => 2500,
            NetworkInterfaceType.FastEthernetFx => 2500,
            NetworkInterfaceType.FastEthernetT => 2500,
            NetworkInterfaceType.Wireless80211 => 2000,
            _ => 0
        };


    private async Task<SyncTcpFrame> ReadRequiredAsync(Stream stream, SyncTcpMessageType expectedType, CancellationToken ct, int timeoutSeconds = SyncConstants.SyncTcpIdleTimeoutSeconds)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var frame = await SyncTcpFrameIo.ReadAsync(stream, timeout.Token) ?? throw new EndOfStreamException();
        if (frame.Type == SyncTcpMessageType.Error)
        {
            var error = frame.Parse(SyncError.Parser);
            throw new InvalidDataException(string.IsNullOrWhiteSpace(error.Message) ? error.Code : error.Message);
        }

        if (frame.Type != expectedType)
            throw new InvalidDataException($"Unexpected sync TCP frame type. Expected={expectedType}, Actual={frame.Type}.");

        return frame;
    }


    private async Task WriteFrameAsync(Stream stream, SyncTcpMessageType type, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.SyncTcpWriteTimeoutSeconds));
        await SyncTcpFrameIo.WriteAsync(stream, type, timeout.Token);
    }


    private async Task WriteFrameAsync(Stream stream, SyncTcpMessageType type, IMessage message, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.SyncTcpWriteTimeoutSeconds));
        await SyncTcpFrameIo.WriteAsync(stream, type, message, timeout.Token);
    }


    private bool ValidatePinnedServerCertificate(X509Certificate? cert, string serverFingerprintHex)
    {
        if (cert is null)
            return false;

        var fingerprint = FingerprintUtil.Normalize(_identity.GetFingerprintHex(new X509Certificate2(cert)));
        var expected = FingerprintUtil.Normalize(serverFingerprintHex);
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




    }
