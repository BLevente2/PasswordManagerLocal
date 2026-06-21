using Google.Protobuf;
using Makaretu.Dns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Persistence;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Utils;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services;

public sealed class DeviceEnrollmentService : IDeviceEnrollmentService, IDisposable
{
    private sealed class EnrollmentSession
    {
        public string SessionId { get; set; } = string.Empty;
        public byte[] Secret { get; set; } = [];
        public string Code { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
        public DeviceEnrollmentState State { get; set; } = DeviceEnrollmentState.Waiting;
        public string? ErrorMessage { get; set; }
        public DeviceEnrollmentErrorCode ErrorCode { get; set; } = DeviceEnrollmentErrorCode.Unknown;
        public int FailedValidationAttempts { get; set; }
        public ServiceDiscovery? Discovery { get; set; }
        public ServiceProfile? Profile { get; set; }
    }

    private sealed class EnrollmentEndpoint
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public Guid DeviceId { get; set; }
        public string TlsCertFingerprint { get; set; } = string.Empty;
        public byte[] SignPublicKey { get; set; } = [];
        public byte[] AgreementPublicKey { get; set; } = [];
        public DeviceType DeviceType { get; set; }
    }


    private sealed class LocalEnrollmentHostCandidate
    {
        public IPAddress Address { get; set; } = IPAddress.None;
        public int Priority { get; set; }
        public string InterfaceName { get; set; } = string.Empty;
        public string InterfaceDescription { get; set; } = string.Empty;
        public bool IsVirtualAdapter { get; set; }
        public bool HasGateway { get; set; }
    }


    private sealed class LocalIpv4Network
    {
        public IPAddress Address { get; set; } = IPAddress.None;
        public IPAddress Mask { get; set; } = IPAddress.None;
        public string InterfaceName { get; set; } = string.Empty;
        public bool IsVirtualAdapter { get; set; }
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _identity;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly ISyncTransportClientService _syncTransport;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly object _lock = new();
    private EnrollmentSession? _currentSession;
    private CancellationTokenSource? _enrollmentExpirationCancellation;

    public DeviceEnrollmentService(IServiceScopeFactory scopeFactory, IDeviceIdentityService identity, IDiscoveredDeviceEndpointCache endpointCache, ISyncTransportClientService syncTransport, ISyncRuntimeService syncRuntime)
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
        _endpointCache = endpointCache;
        _syncTransport = syncTransport;
        _syncRuntime = syncRuntime;
    }




    public async Task<DeviceEnrollmentCodeResponse> StartEnrollmentAsync(CancellationToken ct = default)
    {
        await _syncRuntime.BeginEnrollmentOnlyAsync(ct);
        try
        {
            lock (_lock)
            {
                CancelEnrollmentExpirationLocked();
                StopAdvertisingLocked();
                var directEndpointInfo = BuildDirectEndpointInfo();
                var generated = DeviceEnrollmentCode.Create(directEndpointInfo);
                var session = new EnrollmentSession
                {
                    SessionId = generated.SessionId,
                    Secret = generated.Secret,
                    Code = generated.Code,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                    State = DeviceEnrollmentState.Waiting
                };
                StartAdvertisingLocked(session, directEndpointInfo.Hosts);
                _currentSession = session;
                StartEnrollmentExpirationCountdownLocked(session);
                return new DeviceEnrollmentCodeResponse { Code = session.Code, ExpiresAt = session.ExpiresAt };
            }
        }
        catch
        {
            await _syncRuntime.EndEnrollmentOnlyAsync(CancellationToken.None);
            throw;
        }
    }


    public async Task<DeviceEnrollmentStatusResponse> GetEnrollmentStatusAsync(CancellationToken ct = default)
    {
        DeviceEnrollmentStatusResponse response;
        var endTemporaryMode = false;
        lock (_lock)
        {
            if (_currentSession is null)
                return new DeviceEnrollmentStatusResponse { State = DeviceEnrollmentState.None };
            ExpireSessionIfNeededLocked();
            response = new DeviceEnrollmentStatusResponse
            {
                State = _currentSession.State,
                ErrorMessage = _currentSession.ErrorMessage,
                ErrorCode = _currentSession.ErrorCode,
                ExpiresAt = _currentSession.ExpiresAt
            };
            endTemporaryMode = _currentSession.State is DeviceEnrollmentState.Expired or DeviceEnrollmentState.Failed or DeviceEnrollmentState.Completed;
        }
        if (endTemporaryMode)
            await _syncRuntime.EndEnrollmentOnlyAsync(ct);
        return response;
    }


    public async Task CancelEnrollmentAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            CancelEnrollmentExpirationLocked();
            StopAdvertisingLocked();
            _currentSession = null;
        }
        await _syncRuntime.EndEnrollmentOnlyAsync(ct);
    }


    public async Task AddDeviceByCodeAsync(Guid token, string code, CancellationToken ct = default)
    {
        using (var authorizationScope = _scopeFactory.CreateScope())
        {
            var users = authorizationScope.ServiceProvider.GetRequiredService<IUserService>();
            var localUsers = authorizationScope.ServiceProvider.GetRequiredService<ILocalUserDeviceRepository>();
            var user = await users.GetAndVerifyUserAsync(token, ct);
            if (!await localUsers.IsSyncOnAsync(user.UId, ct))
                throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.SyncDisabled, "Synchronization is disabled for this profile on the current device.");
        }

        DeviceEnrollmentParsedCode parsed;
        try
        {
            parsed = DeviceEnrollmentCode.Parse(code);
        }
        catch (InvalidDataException ex)
        {
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.InvalidCode, ex.Message, ex);
        }

        var directEndpointCandidates = parsed.DirectEndpoints
            .Select(ToEnrollmentEndpoint)
            .Where(endpoint => !IsLocalEndpoint(endpoint))
            .GroupBy(endpoint => $"{endpoint.Host}:{endpoint.Port}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(endpoint => new { Endpoint = endpoint, Priority = GetDirectEndpointPriorityForThisDevice(endpoint) })
            .ToList();

        var directEndpoints = directEndpointCandidates
            .Where(candidate => candidate.Priority > int.MinValue)
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Endpoint.Host, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Endpoint)
            .ToList();

        var hasAuthoritativeSameSubnetDirectEndpoint = directEndpointCandidates.Any(candidate => candidate.Priority >= 2000);
        var directFailures = new List<string>();
        var mdnsFailures = new List<string>();

        DeviceEnrollmentTrace.Info($"AddDeviceByCode started. Session={parsed.SessionId}, directEndpointCount={directEndpoints.Count}, authoritativeDirect={hasAuthoritativeSameSubnetDirectEndpoint}, directEndpointCandidates={string.Join(", ", directEndpointCandidates.Select(e => $"{e.Endpoint.Host}:{e.Endpoint.Port}/priority={e.Priority}"))}");

        foreach (var endpoint in directEndpoints)
        {
            try
            {
                DeviceEnrollmentTrace.Info($"Trying direct enrollment endpoint {endpoint.Host}:{endpoint.Port}.");
                await CompleteEnrollmentWithEndpointAsync(token, parsed, endpoint, ct);
                DeviceEnrollmentTrace.Info($"Direct enrollment endpoint {endpoint.Host}:{endpoint.Port} completed successfully.");
                return;
            }
            catch (DeviceEnrollmentException ex) when (ex.ErrorCode == DeviceEnrollmentErrorCode.NewDeviceConnectionFailed)
            {
                directFailures.Add($"{endpoint.Host}:{endpoint.Port} -> {ex.Message}");
                DeviceEnrollmentTrace.Error($"Direct enrollment endpoint {endpoint.Host}:{endpoint.Port} failed with a connection/transfer error: {ex.Message}", ex);
            }
        }

        if (directEndpoints.Count > 0)
        {
            var reason = hasAuthoritativeSameSubnetDirectEndpoint
                ? "The same-subnet direct endpoint did not respond. Trying mDNS fallback because Wi-Fi/wired bridges can still expose another usable address."
                : "The direct enrollment endpoints did not respond. Trying mDNS fallback.";

            DeviceEnrollmentTrace.Info(reason);
        }

        IReadOnlyList<EnrollmentEndpoint> mdnsEndpoints;
        try
        {
            DeviceEnrollmentTrace.Info("Trying mDNS enrollment discovery fallback.");
            mdnsEndpoints = await FindEnrollmentEndpointsAsync(parsed, ct);
        }
        catch (DeviceEnrollmentException ex) when (directFailures.Count > 0)
        {
            throw new DeviceEnrollmentException(
                DeviceEnrollmentErrorCode.NewDeviceConnectionFailed,
                BuildEndpointFailureMessage(
                    BuildMdnsDiscoveryFailedMessage(hasAuthoritativeSameSubnetDirectEndpoint),
                    directFailures),
                ex);
        }

        foreach (var endpoint in mdnsEndpoints)
        {
            try
            {
                DeviceEnrollmentTrace.Info($"Trying mDNS enrollment endpoint {endpoint.Host}:{endpoint.Port}.");
                await CompleteEnrollmentWithEndpointAsync(token, parsed, endpoint, ct);
                DeviceEnrollmentTrace.Info($"mDNS enrollment endpoint {endpoint.Host}:{endpoint.Port} completed successfully.");
                return;
            }
            catch (DeviceEnrollmentException ex) when (ex.ErrorCode == DeviceEnrollmentErrorCode.NewDeviceConnectionFailed)
            {
                mdnsFailures.Add($"{endpoint.Host}:{endpoint.Port} -> {ex.Message}");
                DeviceEnrollmentTrace.Error($"mDNS enrollment endpoint {endpoint.Host}:{endpoint.Port} failed with a connection/transfer error: {ex.Message}", ex);
            }
        }

        throw new DeviceEnrollmentException(
            DeviceEnrollmentErrorCode.NewDeviceConnectionFailed,
            BuildEndpointFailureMessage(
                BuildNoEndpointAcceptedMessage(hasAuthoritativeSameSubnetDirectEndpoint),
                directFailures.Concat(mdnsFailures)));
    }


    private static string BuildEndpointFailureMessage(string message, IEnumerable<string> endpointFailures)
    {
        var failures = endpointFailures
            .Where(failure => !string.IsNullOrWhiteSpace(failure))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (failures.Count == 0)
            return message;

        return $"{message} Attempted endpoints: {string.Join("; ", failures)}";
    }


    private static string BuildNoEndpointAcceptedMessage(bool hadSameSubnetDirectEndpoint)
    {
        if (hadSameSubnetDirectEndpoint)
        {
            return "The enrollment code contained a same-subnet address for the new device, but the app could not connect to it by direct TCP or mDNS. If general LAN traffic works between the devices, the most likely cause is that this app or TCP port 26688 is blocked by Windows Firewall on the target device, or the sync TCP listener is not running on that device.";
        }

        return "The new device was discovered, but none of the reachable network addresses accepted the enrollment transfer.";
    }


    private static string BuildMdnsDiscoveryFailedMessage(bool hadSameSubnetDirectEndpoint)
    {
        if (hadSameSubnetDirectEndpoint)
        {
            return "The enrollment code contained a same-subnet address for the new device, but the app could not connect to it directly and mDNS discovery also failed. If general LAN traffic works between the devices, the most likely cause is that this app or TCP port 26688 is blocked by Windows Firewall on the target device, or the sync TCP listener is not running on that device.";
        }

        return "The new device was included in the enrollment code, but it could not be reached directly and mDNS discovery also failed.";
    }


    private async Task CompleteEnrollmentWithEndpointAsync(Guid token, DeviceEnrollmentParsedCode parsed, EnrollmentEndpoint endpoint, CancellationToken ct)
    {
        DeviceEnrollmentTrace.Info($"Enrollment endpoint check started for {endpoint.Host}:{endpoint.Port}. HasEmbeddedIdentity={endpoint.DeviceId != Guid.Empty}.");
        await EnsureEndpointTcpReachableAsync(endpoint, ct);
        DeviceEnrollmentTrace.Info($"TCP connection check succeeded for {endpoint.Host}:{endpoint.Port}.");
        endpoint = await ResolveEndpointIdentityAsync(endpoint, parsed, ct);
        DeviceEnrollmentTrace.Info($"Enrollment identity resolved for {endpoint.Host}:{endpoint.Port}. DeviceId={endpoint.DeviceId}, TlsFingerprintPrefix={NormalizeFingerprint(endpoint.TlsCertFingerprint)[..Math.Min(16, NormalizeFingerprint(endpoint.TlsCertFingerprint).Length)]}.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        var cache = scope.ServiceProvider.GetRequiredService<IDataCachingService>();
        var syncIdentities = scope.ServiceProvider.GetRequiredService<ISyncDeviceIdentityService>();
        var syncTasks = scope.ServiceProvider.GetRequiredService<IDeviceSyncTaskService>();
        var user = await users.GetAndVerifyUserAsync(token, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            await RegisterRemoteDeviceAsync(scope.ServiceProvider, user.UId, endpoint, ct);
            await EnsureEncryptedDeviceDataAsync(users, user, token, endpoint.DeviceId, ct);
            var snapshot = await BuildSnapshotAsync(scope.ServiceProvider, user.UId, ct);

            var proof = DeviceEnrollmentCode.BuildCompletionProof(
                parsed.SessionId,
                parsed.Secret,
                _identity.LocalDeviceId.ToString("N"),
                _identity.SignPublicKey,
                _identity.FingerprintHex);

            var result = await SendEnrollmentSnapshotAsync(endpoint, parsed.SessionId, parsed.Secret, proof, snapshot, ct);
            if (!result.Ok)
                throw new DeviceEnrollmentException(result.ErrorCode, result.Error ?? "The new device rejected the enrollment request.");

            await QueueInitialSyncAsync(scope.ServiceProvider, user.UId, endpoint.DeviceId, ct);
            await transaction.CommitAsync(ct);
            cache.InvalidateToken(token);

            var remoteDevice = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == endpoint.DeviceId, ct);
            if (remoteDevice is not null)
            {
                syncIdentities.TryAdd(remoteDevice);
                var discoveredEndpoint = new DiscoveredDeviceEndpoint
                {
                    Host = endpoint.Host,
                    Port = endpoint.Port,
                    TlsCertFingerprint = endpoint.TlsCertFingerprint
                };
                _endpointCache.AddOrUpdate(discoveredEndpoint);
                syncTasks.TryStart(discoveredEndpoint, remoteDevice);
            }
        }
        catch (DeviceEnrollmentException)
        {
            syncIdentities.TryRemove(new Device
            {
                Id = endpoint.DeviceId,
                TlsCertFingerprint = endpoint.TlsCertFingerprint
            });
            _endpointCache.TryRemove(endpoint.TlsCertFingerprint);
            throw;
        }
        catch (Exception ex)
        {
            syncIdentities.TryRemove(new Device
            {
                Id = endpoint.DeviceId,
                TlsCertFingerprint = endpoint.TlsCertFingerprint
            });
            _endpointCache.TryRemove(endpoint.TlsCertFingerprint);
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.Unknown, ex.Message, ex);
        }
    }


    private static async Task EnsureEndpointTcpReachableAsync(EnrollmentEndpoint endpoint, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(DeviceEnrollmentConnectTimeoutSeconds));
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or ArgumentException)
        {
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.NewDeviceConnectionFailed, $"The new device could not be reached at {endpoint.Host}:{endpoint.Port}.", ex);
        }
    }


    public async Task<DeviceEnrollmentInfoResponse> GetIncomingEnrollmentInfoAsync(string sessionId, byte[] codeProof, CancellationToken ct = default)
    {
        DeviceEnrollmentInfoResponse response;
        var endTemporaryMode = false;

        lock (_lock)
        {
            ExpireSessionIfNeededLocked();
            var session = _currentSession;

            if (session is null || session.State != DeviceEnrollmentState.Waiting)
            {
                endTemporaryMode = session?.State is DeviceEnrollmentState.Expired or DeviceEnrollmentState.Failed or DeviceEnrollmentState.Completed;
                response = new DeviceEnrollmentInfoResponse
                {
                    Ok = false,
                    ErrorCode = session?.ErrorCode ?? DeviceEnrollmentErrorCode.NewDeviceRejected,
                    Error = session?.ErrorMessage ?? "No active enrollment session was found."
                };
            }
            else if (!string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
            {
                var failure = RegisterFailedEnrollmentValidationLocked(
                    session,
                    DeviceEnrollmentErrorCode.CodeProofInvalid,
                    "The enrollment session does not match the displayed code.");
                endTemporaryMode = failure.LimitReached;
                response = new DeviceEnrollmentInfoResponse
                {
                    Ok = false,
                    ErrorCode = DeviceEnrollmentErrorCode.CodeProofInvalid,
                    Error = failure.Message
                };
            }
            else
            {
                var expectedProof = DeviceEnrollmentCode.BuildEnrollmentInfoProof(sessionId, session.Secret);
                if (!DeviceEnrollmentCode.FixedTimeEquals(expectedProof, codeProof))
                {
                    var failure = RegisterFailedEnrollmentValidationLocked(
                        session,
                        DeviceEnrollmentErrorCode.CodeProofInvalid,
                        "The enrollment code proof is invalid.");
                    endTemporaryMode = failure.LimitReached;
                    response = new DeviceEnrollmentInfoResponse
                    {
                        Ok = false,
                        ErrorCode = DeviceEnrollmentErrorCode.CodeProofInvalid,
                        Error = failure.Message
                    };
                }
                else
                {
                    response = new DeviceEnrollmentInfoResponse
                    {
                        Ok = true,
                        DeviceId = _identity.LocalDeviceId,
                        DeviceType = _identity.DeviceType,
                        TlsCertFingerprint = _identity.FingerprintHex,
                        SignPublicKey = _identity.SignPublicKey,
                        AgreementPublicKey = _identity.AgreementPublicKey
                    };
                }
            }
        }

        if (endTemporaryMode)
            await _syncRuntime.EndEnrollmentOnlyAsync(CancellationToken.None);

        return response;
    }


    public async Task<string> RegisterIncomingEnrollmentValidationFailureAsync(
        DeviceEnrollmentErrorCode errorCode,
        string message,
        CancellationToken ct = default)
    {
        var endEnrollmentOnlyMode = false;
        var responseMessage = message;

        lock (_lock)
        {
            ExpireSessionIfNeededLocked();
            var current = _currentSession;

            if (current is not null && current.State == DeviceEnrollmentState.Waiting)
            {
                var failure = RegisterFailedEnrollmentValidationLocked(current, errorCode, message);
                endEnrollmentOnlyMode = failure.LimitReached;
                responseMessage = failure.Message;
            }
            else if (current?.ErrorMessage is not null)
            {
                responseMessage = current.ErrorMessage;
            }
        }

        if (endEnrollmentOnlyMode)
            await _syncRuntime.EndEnrollmentOnlyAsync(CancellationToken.None);

        return responseMessage;
    }


    public async Task<(bool Ok, DeviceEnrollmentErrorCode ErrorCode, string? Error)> CompleteIncomingEnrollmentAsync(
        string sessionId,
        byte[] codeProof,
        byte[] snapshotBytes,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsCertFingerprint,
        string actualClientTlsCertFingerprint,
        string? sourceHost,
        int snapshotEncryptionVersion,
        byte[] snapshotEncryptionNonce,
        byte[] snapshotEncryptionTag,
        CancellationToken ct = default)
    {
        EnrollmentSession? session;
        (DeviceEnrollmentErrorCode Code, string Message)? earlyFailure = null;
        var endTemporaryMode = false;

        lock (_lock)
        {
            ExpireSessionIfNeededLocked();
            session = _currentSession;

            if (session is null || session.State != DeviceEnrollmentState.Waiting)
            {
                earlyFailure = (
                    session?.ErrorCode ?? DeviceEnrollmentErrorCode.NewDeviceRejected,
                    session?.ErrorMessage ?? "No active enrollment session was found.");
                endTemporaryMode = session?.State is DeviceEnrollmentState.Expired or DeviceEnrollmentState.Failed or DeviceEnrollmentState.Completed;
            }
            else if (!string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
            {
                var failure = RegisterFailedEnrollmentValidationLocked(
                    session,
                    DeviceEnrollmentErrorCode.CodeProofInvalid,
                    "The enrollment session does not match the displayed code.");
                earlyFailure = (DeviceEnrollmentErrorCode.CodeProofInvalid, failure.Message);
                endTemporaryMode = failure.LimitReached;
            }
        }

        if (earlyFailure is not null)
        {
            if (endTemporaryMode)
                await _syncRuntime.EndEnrollmentOnlyAsync(CancellationToken.None);
            return (false, earlyFailure.Value.Code, earlyFailure.Value.Message);
        }

        var activeSession = session!;

        if (!string.Equals(NormalizeFingerprint(sourceTlsCertFingerprint), NormalizeFingerprint(actualClientTlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
        {
            DeviceEnrollmentTrace.Error($"Incoming enrollment rejected because client TLS fingerprint did not match. Expected={NormalizeFingerprint(sourceTlsCertFingerprint)}, Actual={NormalizeFingerprint(actualClientTlsCertFingerprint)}.");
            return await RejectIncomingValidationAsync(DeviceEnrollmentErrorCode.NewDeviceRejected, "The source device TLS certificate does not match the enrollment request.");
        }

        var expectedProof = DeviceEnrollmentCode.BuildCompletionProof(
            sessionId,
            activeSession.Secret,
            sourceDeviceId,
            sourceSignPublicKey,
            sourceTlsCertFingerprint);

        if (!DeviceEnrollmentCode.FixedTimeEquals(expectedProof, codeProof))
            return await RejectIncomingValidationAsync(DeviceEnrollmentErrorCode.CodeProofInvalid, "The enrollment code proof is invalid.");

        byte[] plaintextSnapshotBytes;
        try
        {
            plaintextSnapshotBytes = DecryptEnrollmentSnapshot(
                sessionId,
                activeSession.Secret,
                snapshotBytes,
                sourceDeviceId,
                sourceSignPublicKey,
                sourceTlsCertFingerprint,
                snapshotEncryptionVersion,
                snapshotEncryptionNonce,
                snapshotEncryptionTag);
        }
        catch (CryptographicException ex)
        {
            return await RejectIncomingValidationAsync(DeviceEnrollmentErrorCode.ProfileDataInvalid, $"The encrypted enrollment snapshot could not be authenticated: {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            return await RejectIncomingValidationAsync(DeviceEnrollmentErrorCode.ProfileDataInvalid, ex.Message);
        }

        DeviceEnrollmentSnapshot? snapshot;
        try
        {
            RejectSensitiveLocalOnlySnapshotPayload(plaintextSnapshotBytes);
            snapshot = JsonSerializer.Deserialize<DeviceEnrollmentSnapshot>(plaintextSnapshotBytes);
        }
        catch (JsonException ex)
        {
            return await RejectIncomingValidationAsync(DeviceEnrollmentErrorCode.ProfileDataInvalid, $"The received profile data is invalid: {ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            return await RejectIncomingValidationAsync(DeviceEnrollmentErrorCode.ProfileDataInvalid, ex.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextSnapshotBytes);
        }

        if (snapshot is null || snapshot.PrimaryUserId == Guid.Empty)
            return await RejectIncomingValidationAsync(DeviceEnrollmentErrorCode.ProfileDataInvalid, "The received profile data is empty.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            DeviceEnrollmentTrace.Info($"Importing incoming enrollment snapshot. Users={snapshot.Users.Count}, Groups={snapshot.Groups.Count}, Devices={snapshot.Devices.Count}, UserDevices={snapshot.UserDevices.Count}.");
            await ImportSnapshotAsync(scope.ServiceProvider, snapshot, ct);
            await _syncRuntime.RefreshSyncEnabledAsync(ct);
            await CacheIncomingEnrollmentSourceEndpointAsync(scope.ServiceProvider, sourceDeviceId, sourceTlsCertFingerprint, sourceHost, ct);
            DeviceEnrollmentTrace.Info("Incoming enrollment snapshot import completed successfully.");
        }
        catch (Exception ex)
        {
            DeviceEnrollmentTrace.Error($"Incoming enrollment snapshot import failed: {ex.Message}", ex);
            return await RejectIncomingValidationAsync(DeviceEnrollmentErrorCode.ProfileDataInvalid, ex.Message);
        }

        lock (_lock)
        {
            if (_currentSession is not null)
            {
                _currentSession.State = DeviceEnrollmentState.Completed;
                _currentSession.ErrorCode = DeviceEnrollmentErrorCode.Unknown;
                _currentSession.ErrorMessage = null;
                CancelEnrollmentExpirationLocked();
                StopAdvertisingLocked();
            }
        }

        await _syncRuntime.EndEnrollmentOnlyAsync(ct);
        return (true, DeviceEnrollmentErrorCode.Unknown, null);

        async Task<(bool Ok, DeviceEnrollmentErrorCode ErrorCode, string? Error)> RejectIncomingValidationAsync(DeviceEnrollmentErrorCode errorCode, string message)
        {
            var responseMessage = await RegisterIncomingEnrollmentValidationFailureAsync(errorCode, message, CancellationToken.None);
            return (false, errorCode, responseMessage);
        }
    }


    private (bool LimitReached, string Message) RegisterFailedEnrollmentValidationLocked(
        EnrollmentSession session,
        DeviceEnrollmentErrorCode errorCode,
        string message)
    {
        session.FailedValidationAttempts++;
        var remainingAttempts = Math.Max(0, SyncConstants.MaxEnrollmentValidationAttempts - session.FailedValidationAttempts);

        if (remainingAttempts == 0)
        {
            session.State = DeviceEnrollmentState.Failed;
            session.ErrorCode = errorCode;
            session.ErrorMessage = "Enrollment was stopped after three failed validation attempts. Generate a new enrollment code before trying again.";
            CancelEnrollmentExpirationLocked();
            StopAdvertisingLocked();
            return (true, session.ErrorMessage);
        }

        var suffix = remainingAttempts == 1
            ? "1 validation attempt remains."
            : $"{remainingAttempts} validation attempts remain.";
        return (false, $"{message} {suffix}");
    }


    private DeviceEnrollmentDirectEndpointInfo BuildDirectEndpointInfo() =>
        new()
        {
            DeviceId = _identity.LocalDeviceId,
            TlsCertFingerprint = _identity.FingerprintHex,
            SignPublicKey = _identity.SignPublicKey,
            AgreementPublicKey = _identity.AgreementPublicKey,
            Port = SyncPort,
            Hosts = GetLocalEnrollmentHosts()
        };


    private static IReadOnlyList<string> GetLocalEnrollmentHosts()
    {
        var candidates = new List<LocalEnrollmentHostCandidate>();

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var properties = networkInterface.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(gateway => IsUsableUnicastAddress(gateway.Address));
                var isVirtualAdapter = IsVirtualOrNonLanAdapter(networkInterface);
                var typePriority = GetNetworkInterfaceTypePriority(networkInterface.NetworkInterfaceType);

                foreach (var addressInfo in properties.UnicastAddresses)
                {
                    var address = addressInfo.Address;
                    if (!IsUsableUnicastAddress(address))
                        continue;

                    if (IsWindowsHostOnlyGatewayAddress(address, isVirtualAdapter))
                        continue;

                    var priority = 0;

                    if (!isVirtualAdapter)
                        priority += 10000;
                    else
                        priority -= 10000;

                    if (hasGateway)
                        priority += 4000;

                    priority += typePriority;

                    if (address.AddressFamily == AddressFamily.InterNetwork)
                        priority += 2000;
                    else
                        priority -= 500;

                    priority += GetPrivateAddressPriority(address);

                    if (OperatingSystem.IsWindows() && (addressInfo.PrefixOrigin is PrefixOrigin.Dhcp or PrefixOrigin.Manual))
                        priority += 250;

                    candidates.Add(new LocalEnrollmentHostCandidate
                    {
                        Address = address,
                        Priority = priority,
                        InterfaceName = networkInterface.Name,
                        InterfaceDescription = networkInterface.Description,
                        IsVirtualAdapter = isVirtualAdapter,
                        HasGateway = hasGateway
                    });
                }
            }
        }
        catch (Exception ex)
        {
            DeviceEnrollmentTrace.Error($"Could not enumerate local enrollment network interfaces: {ex.Message}", ex);
        }

        var hasPhysicalIpv4Candidate = candidates.Any(candidate =>
            !candidate.IsVirtualAdapter &&
            candidate.Address.AddressFamily == AddressFamily.InterNetwork &&
            IsPrivateIpv4(candidate.Address));

        if (hasPhysicalIpv4Candidate)
        {
            candidates = candidates
                .Where(candidate => !candidate.IsVirtualAdapter && candidate.Address.AddressFamily == AddressFamily.InterNetwork)
                .ToList();
        }
        else if (candidates.Any(candidate => !candidate.IsVirtualAdapter))
        {
            candidates = candidates
                .Where(candidate => !candidate.IsVirtualAdapter)
                .ToList();
        }
        else
        {
            DeviceEnrollmentTrace.Info("Only virtual/VPN/non-LAN enrollment address candidates were found. Keeping them as a last-resort fallback.");
        }

        if (candidates.Count == 0)
        {
            try
            {
                foreach (var address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (!IsUsableUnicastAddress(address) || IsWindowsHostOnlyGatewayAddress(address, true))
                        continue;

                    candidates.Add(new LocalEnrollmentHostCandidate
                    {
                        Address = address,
                        Priority = address.AddressFamily == AddressFamily.InterNetwork ? 100 : 50,
                        InterfaceName = "DNS fallback",
                        InterfaceDescription = "DNS fallback",
                        IsVirtualAdapter = false,
                        HasGateway = false
                    });
                }
            }
            catch (Exception ex)
            {
                DeviceEnrollmentTrace.Error($"Could not enumerate DNS fallback enrollment addresses: {ex.Message}", ex);
            }
        }

        var hosts = candidates
            .GroupBy(candidate => candidate.Address.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.Priority).First())
            .OrderByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Address.ToString(), StringComparer.Ordinal)
            .ToList();

        DeviceEnrollmentTrace.Info($"Local enrollment hosts selected: {string.Join(", ", hosts.Select(candidate => $"{candidate.Address} on {candidate.InterfaceName} priority={candidate.Priority} virtual={candidate.IsVirtualAdapter} gateway={candidate.HasGateway}"))}");

        return hosts
            .Select(candidate => candidate.Address.ToString())
            .ToList();
    }


    private static bool IsUsableUnicastAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.IPv6Any))
            return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !IsApipaIpv4(address);

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        return !address.IsIPv6LinkLocal && !address.IsIPv6Multicast && !address.IsIPv6SiteLocal;
    }


    private static bool IsApipaIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }


    private static int GetNetworkInterfaceTypePriority(NetworkInterfaceType interfaceType) =>
        interfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => 3000,
            NetworkInterfaceType.Ethernet => 2500,
            NetworkInterfaceType.GigabitEthernet => 2500,
            NetworkInterfaceType.FastEthernetFx => 2500,
            NetworkInterfaceType.FastEthernetT => 2500,
            NetworkInterfaceType.Ppp => -1000,
            _ => 0
        };


    private static bool IsVirtualOrNonLanAdapter(NetworkInterface networkInterface)
    {
        var text = $"{networkInterface.Name} {networkInterface.Description}".ToLowerInvariant();

        return text.Contains("virtual") ||
               text.Contains("vethernet") ||
               text.Contains("hyper-v") ||
               text.Contains("default switch") ||
               text.Contains("wsl") ||
               text.Contains("docker") ||
               text.Contains("vmware") ||
               text.Contains("virtualbox") ||
               text.Contains("vmnet") ||
               text.Contains("host-only") ||
               text.Contains("loopback") ||
               text.Contains("npcap") ||
               text.Contains("bluetooth") ||
               text.Contains("vpn") ||
               text.Contains("tap") ||
               text.Contains("tun") ||
               text.Contains("tailscale") ||
               text.Contains("zerotier") ||
               text.Contains("wireguard") ||
               text.Contains("pseudo");
    }


    private static bool IsWindowsHostOnlyGatewayAddress(IPAddress address, bool isVirtualAdapter)
    {
        if (!isVirtualAdapter || address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        if (bytes[3] != 1)
            return false;

        return true;
    }


    private static int GetPrivateAddressPriority(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4)
                return 0;

            if (bytes[0] == 192 && bytes[1] == 168)
                return 500;

            if (bytes[0] == 10)
                return 400;

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return 250;

            return -500;
        }

        return IsPrivateIpv6(address) ? 100 : -500;
    }


    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
               (bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168));
    }


    private static bool IsPrivateIpv6(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC;
    }


    private static int GetDirectEndpointPriorityForThisDevice(EnrollmentEndpoint endpoint)
    {
        if (!IPAddress.TryParse(endpoint.Host, out var remoteAddress))
            return 0;

        if (!IsUsableUnicastAddress(remoteAddress))
            return int.MinValue;

        var localNetworks = GetLocalIpv4Networks();
        if (remoteAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            if (localNetworks.Any(network => network.Address.Equals(remoteAddress)))
            {
                DeviceEnrollmentTrace.Info($"Skipping direct enrollment endpoint {endpoint.Host}:{endpoint.Port} because the address belongs to this device.");
                return int.MinValue;
            }

            var samePhysicalSubnet = localNetworks.Any(network => !network.IsVirtualAdapter && IsInSameIpv4Subnet(remoteAddress, network.Address, network.Mask));
            if (samePhysicalSubnet)
                return 5000 + GetPrivateAddressPriority(remoteAddress);

            var sameVirtualSubnet = localNetworks.Any(network => network.IsVirtualAdapter && IsInSameIpv4Subnet(remoteAddress, network.Address, network.Mask));
            if (sameVirtualSubnet)
                return 2500 + GetPrivateAddressPriority(remoteAddress);

            if (IsPrivateIpv4(remoteAddress))
                return 100 + GetPrivateAddressPriority(remoteAddress);
        }

        return remoteAddress.AddressFamily == AddressFamily.InterNetwork ? 0 : -100;
    }


    private static List<LocalIpv4Network> GetLocalIpv4Networks()
    {
        var networks = new List<LocalIpv4Network>();

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var isVirtualAdapter = IsVirtualOrNonLanAdapter(networkInterface);
                foreach (var addressInfo in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (addressInfo.Address.AddressFamily != AddressFamily.InterNetwork ||
                        addressInfo.IPv4Mask is null ||
                        !IsUsableUnicastAddress(addressInfo.Address))
                        continue;

                    networks.Add(new LocalIpv4Network
                    {
                        Address = addressInfo.Address,
                        Mask = addressInfo.IPv4Mask,
                        InterfaceName = networkInterface.Name,
                        IsVirtualAdapter = isVirtualAdapter
                    });
                }
            }
        }
        catch (Exception ex)
        {
            DeviceEnrollmentTrace.Error($"Could not enumerate local IPv4 networks: {ex.Message}", ex);
        }

        return networks;
    }


    private static bool IsInSameIpv4Subnet(IPAddress remoteAddress, IPAddress localAddress, IPAddress mask)
    {
        if (remoteAddress.AddressFamily != AddressFamily.InterNetwork ||
            localAddress.AddressFamily != AddressFamily.InterNetwork ||
            mask.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var remoteBytes = remoteAddress.GetAddressBytes();
        var localBytes = localAddress.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        if (remoteBytes.Length != 4 || localBytes.Length != 4 || maskBytes.Length != 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            if ((remoteBytes[i] & maskBytes[i]) != (localBytes[i] & maskBytes[i]))
                return false;
        }

        return true;
    }


    private EnrollmentEndpoint ToEnrollmentEndpoint(DeviceEnrollmentParsedDirectEndpoint endpoint) =>
        new()
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            DeviceId = endpoint.DeviceId,
            TlsCertFingerprint = endpoint.TlsCertFingerprint,
            SignPublicKey = endpoint.SignPublicKey,
            AgreementPublicKey = endpoint.AgreementPublicKey
        };


    private bool IsLocalEndpoint(EnrollmentEndpoint endpoint) =>
        endpoint.DeviceId == _identity.LocalDeviceId || _identity.SignPublicKey.SequenceEqual(endpoint.SignPublicKey);


    private void StartAdvertisingLocked(EnrollmentSession session, IReadOnlyList<string> directHosts)
    {
        var advertiseHash = DeviceEnrollmentCode.BuildEnrollmentAdvertisementHash(
            session.SessionId,
            session.Secret,
            _identity.FingerprintHex,
            _identity.SignPublicKey);

        session.Discovery = new ServiceDiscovery();
        session.Profile = new ServiceProfile($"pml-enroll-{session.SessionId.ToLowerInvariant()}", MdnsServiceType, (ushort)SyncPort);
        session.Profile.AddProperty("deviceid", _identity.DeviceIdHex);
        session.Profile.AddProperty("deviceguid", _identity.LocalDeviceId.ToString("N"));
        session.Profile.AddProperty("signpub", Convert.ToHexString(_identity.SignPublicKey));
        session.Profile.AddProperty("agreepub", Convert.ToHexString(_identity.AgreementPublicKey));
        session.Profile.AddProperty("tlsfp", _identity.FingerprintHex);
        session.Profile.AddProperty("enrollid", session.SessionId);
        session.Profile.AddProperty("enrollhash", advertiseHash);
        session.Profile.AddProperty("hosts", string.Join(",", directHosts.Where(host => IPAddress.TryParse(host, out _)).Distinct(StringComparer.OrdinalIgnoreCase)));

        session.Discovery.Advertise(session.Profile);
        session.Discovery.Announce(session.Profile);
    }


    private void StartEnrollmentExpirationCountdownLocked(EnrollmentSession session)
    {
        CancelEnrollmentExpirationLocked();
        var cancellation = new CancellationTokenSource();
        _enrollmentExpirationCancellation = cancellation;
        _ = ExpireEnrollmentSessionAsync(session.SessionId, session.ExpiresAt, cancellation);
    }


    private async Task ExpireEnrollmentSessionAsync(string sessionId, DateTimeOffset expiresAt, CancellationTokenSource cancellation)
    {
        try
        {
            var ct = cancellation.Token;
            var delay = expiresAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);

            var expired = false;
            lock (_lock)
            {
                if (!ct.IsCancellationRequested &&
                    _currentSession is not null &&
                    string.Equals(_currentSession.SessionId, sessionId, StringComparison.Ordinal) &&
                    _currentSession.State == DeviceEnrollmentState.Waiting)
                {
                    _currentSession.State = DeviceEnrollmentState.Expired;
                    _currentSession.ErrorCode = DeviceEnrollmentErrorCode.CodeExpired;
                    _currentSession.ErrorMessage = "The enrollment code expired.";
                    _enrollmentExpirationCancellation = null;
                    StopAdvertisingLocked();
                    expired = true;
                }
            }

            if (expired)
                await _syncRuntime.EndEnrollmentOnlyAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }


    private void CancelEnrollmentExpirationLocked()
    {
        var cancellation = _enrollmentExpirationCancellation;
        _enrollmentExpirationCancellation = null;
        if (cancellation is null)
            return;

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        cancellation.Dispose();
    }


    private void StopAdvertisingLocked()
    {
        if (_currentSession?.Discovery is not null)
        {
            if (_currentSession.Profile is not null)
                _currentSession.Discovery.Unadvertise(_currentSession.Profile);

            _currentSession.Discovery.Dispose();
            _currentSession.Discovery = null;
            _currentSession.Profile = null;
        }
    }


    private void ExpireSessionIfNeededLocked()
    {
        if (_currentSession is null || _currentSession.State != DeviceEnrollmentState.Waiting)
            return;

        if (DateTimeOffset.UtcNow <= _currentSession.ExpiresAt)
            return;

        _currentSession.State = DeviceEnrollmentState.Expired;
        _currentSession.ErrorCode = DeviceEnrollmentErrorCode.CodeExpired;
        _currentSession.ErrorMessage = "The enrollment code expired.";
        CancelEnrollmentExpirationLocked();
        StopAdvertisingLocked();
    }


    private async Task<IReadOnlyList<EnrollmentEndpoint>> FindEnrollmentEndpointsAsync(DeviceEnrollmentParsedCode parsed, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<EnrollmentEndpoint>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discovery = new ServiceDiscovery();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var queryLoop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(DeviceEnrollmentDiscoveryTimeoutSeconds));

        discovery.ServiceInstanceDiscovered += OnDiscovered;
        var queryTask = RepeatQueriesAsync(queryLoop.Token);

        using (timeout.Token.Register(() => tcs.TrySetCanceled(timeout.Token)))
        {
            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException ex)
            {
                throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.NewDeviceNotFound, "The new device could not be found on the local network. Keep the code screen open on the new device and make sure both devices are on the same local network.", ex);
            }
            finally
            {
                queryLoop.Cancel();
                discovery.ServiceInstanceDiscovered -= OnDiscovered;

                try
                {
                    await queryTask;
                }
                catch
                {
                }
            }
        }

        async Task RepeatQueriesAsync(CancellationToken queryCt)
        {
            while (!queryCt.IsCancellationRequested && !tcs.Task.IsCompleted)
            {
                try
                {
                    discovery.QueryServiceInstances(MdnsServiceType);
                }
                catch
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(2), queryCt);
            }
        }


        async void OnDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
        {
            try
            {
                var mdns = discovery.Mdns;
                var txt = await ResolveTxtAsync(mdns, e.ServiceInstanceName, timeout.Token);
                if (txt is null)
                    return;

                if (!txt.TryGetValue("enrollid", out var enrollId) || !string.Equals(enrollId, parsed.SessionId, StringComparison.Ordinal))
                    return;

                if (!TryReadEndpointIdentity(txt, out var deviceId, out var tlsFp, out var signPub, out var agreePub))
                    return;

                if (!txt.TryGetValue("enrollhash", out var advertisedHash))
                    return;

                var expectedHash = DeviceEnrollmentCode.BuildEnrollmentAdvertisementHash(parsed.SessionId, parsed.Secret, tlsFp, signPub);
                if (!string.Equals(expectedHash, advertisedHash, StringComparison.Ordinal))
                    return;

                if (deviceId == _identity.LocalDeviceId || _identity.SignPublicKey.SequenceEqual(signPub))
                    return;

                var srv = await ResolveSrvAsync(mdns, e.ServiceInstanceName, timeout.Token);
                if (srv is null)
                    return;

                var hosts = new List<string>();
                if (txt.TryGetValue("hosts", out var advertisedHosts))
                    hosts.AddRange(ParseAdvertisedHosts(advertisedHosts));

                hosts.AddRange(await ResolveHostsAsync(mdns, srv.Target, timeout.Token));

                var endpoints = hosts
                    .Where(host => !string.IsNullOrWhiteSpace(host))
                    .Select(host => host.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(host => new EnrollmentEndpoint
                    {
                        Host = host,
                        Port = srv.Port,
                        DeviceId = deviceId,
                        TlsCertFingerprint = tlsFp,
                        SignPublicKey = signPub,
                        AgreementPublicKey = agreePub
                    })
                    .Where(endpoint => !IsLocalEndpoint(endpoint))
                    .Select(endpoint => new { Endpoint = endpoint, Priority = GetDirectEndpointPriorityForThisDevice(endpoint) })
                    .Where(candidate => candidate.Priority > int.MinValue)
                    .OrderByDescending(candidate => candidate.Priority)
                    .ThenBy(candidate => candidate.Endpoint.Host, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (endpoints.Count == 0)
                    return;

                var bestPriority = endpoints[0].Priority;
                if (bestPriority > 0)
                    endpoints = endpoints.Where(candidate => candidate.Priority > 0).ToList();

                DeviceEnrollmentTrace.Info($"mDNS enrollment endpoints resolved: {string.Join(", ", endpoints.Select(e => $"{e.Endpoint.Host}:{e.Endpoint.Port}/priority={e.Priority}"))}");
                tcs.TrySetResult(endpoints.Select(candidate => candidate.Endpoint).ToList());
            }
            catch (Exception ex)
            {
                DeviceEnrollmentTrace.Error($"mDNS enrollment endpoint processing failed: {ex.Message}", ex);
            }
        }
    }


    private static async Task<IReadOnlyDictionary<string, string>?> ResolveTxtAsync(MulticastService mdns, DomainName instance, CancellationToken ct)
    {
        var query = new Message();
        query.Questions.Add(new Question { Name = instance, Type = DnsType.TXT });

        var response = await mdns.ResolveAsync(query, ct);
        var record = response.Answers.Concat(response.AdditionalRecords).OfType<TXTRecord>().FirstOrDefault();
        if (record is null)
            return null;

        return record.Strings
            .Where(s => s.Contains('='))
            .Select(s =>
            {
                var separatorIndex = s.IndexOf('=');
                return new KeyValuePair<string, string>(s[..separatorIndex], s[(separatorIndex + 1)..]);
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);
    }


    private static async Task<SRVRecord?> ResolveSrvAsync(MulticastService mdns, DomainName instance, CancellationToken ct)
    {
        var query = new Message();
        query.Questions.Add(new Question { Name = instance, Type = DnsType.SRV });

        var response = await mdns.ResolveAsync(query, ct);
        return response.Answers.Concat(response.AdditionalRecords).OfType<SRVRecord>().FirstOrDefault();
    }


    private static IReadOnlyList<string> ParseAdvertisedHosts(string advertisedHosts) =>
        advertisedHosts
            .Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(host => IPAddress.TryParse(host, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();


    private static async Task<IReadOnlyList<string>> ResolveHostsAsync(MulticastService mdns, DomainName target, CancellationToken ct)
    {
        var hosts = new List<string>();

        var aQuery = new Message();
        aQuery.Questions.Add(new Question { Name = target, Type = DnsType.A });

        var aResponse = await mdns.ResolveAsync(aQuery, ct);
        hosts.AddRange(aResponse.Answers.Concat(aResponse.AdditionalRecords).OfType<ARecord>().Select(record => record.Address.ToString()));

        var aaaaQuery = new Message();
        aaaaQuery.Questions.Add(new Question { Name = target, Type = DnsType.AAAA });

        var aaaaResponse = await mdns.ResolveAsync(aaaaQuery, ct);
        hosts.AddRange(aaaaResponse.Answers.Concat(aaaaResponse.AdditionalRecords).OfType<AAAARecord>().Select(record => record.Address.ToString()));

        return hosts
            .Where(host => IPAddress.TryParse(host, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    private static bool TryReadEndpointIdentity(IReadOnlyDictionary<string, string> txt, out Guid deviceId, out string tlsFp, out byte[] signPub, out byte[] agreePub)
    {
        deviceId = Guid.Empty;
        tlsFp = string.Empty;
        signPub = [];
        agreePub = [];

        try
        {
            if (!txt.TryGetValue("deviceguid", out var deviceGuid) || !Guid.TryParseExact(deviceGuid, "N", out deviceId))
                return false;

            if (!txt.TryGetValue("tlsfp", out var tlsFingerprint) || string.IsNullOrWhiteSpace(tlsFingerprint))
                return false;

            tlsFp = tlsFingerprint;

            if (!txt.TryGetValue("signpub", out var signPubHex))
                return false;

            if (!txt.TryGetValue("agreepub", out var agreePubHex))
                return false;

            signPub = Convert.FromHexString(signPubHex);
            agreePub = Convert.FromHexString(agreePubHex);

            return signPub.Length == SyncConstants.SyncDeltaEd25519PublicKeyBytes &&
                   agreePub.Length == SyncConstants.SyncDeltaX25519PublicKeyBytes;
        }
        catch
        {
            return false;
        }
    }


    private async Task RegisterRemoteDeviceAsync(IServiceProvider services, Guid userId, EnrollmentEndpoint endpoint, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var device = await db.Devices
            .Include(d => d.UserDevices)
            .FirstOrDefaultAsync(d => d.Id == endpoint.DeviceId, ct);

        if (device is not null)
        {
            foreach (var existingLink in device.UserDevices)
                existingLink.VerifyIntegrity();
        }

        if (device is null)
        {
            device = new Device
            {
                Id = endpoint.DeviceId,
                PublicKey = endpoint.AgreementPublicKey,
                SignPublicKey = endpoint.SignPublicKey,
                TlsCertFingerprint = endpoint.TlsCertFingerprint,
                DeviceType = endpoint.DeviceType,
                LastSync = now.UtcDateTime,
                LastSeen = now.UtcDateTime,
                IsTrusted = true,
                IsBlocked = false,
                LastModifiedAt = now
            };
            device.GenerateIntegrityHash();
            await db.Devices.AddAsync(device, ct);
        }
        else
        {
            if (!device.SignPublicKey.SequenceEqual(endpoint.SignPublicKey) ||
                !device.PublicKey.SequenceEqual(endpoint.AgreementPublicKey) ||
                !string.Equals(NormalizeFingerprint(device.TlsCertFingerprint), NormalizeFingerprint(endpoint.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase) ||
                device.DeviceType != endpoint.DeviceType)
                throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.DeviceIdentityConflict, "A different device already uses this device identity.");

            device.IsTrusted = true;
            device.IsBlocked = false;
            device.BlockedReason = null;
            device.BlockedAt = null;
            device.LastSeen = now.UtcDateTime;
            device.LastModifiedAt = now;
            device.GenerateIntegrityHash();
        }

        var link = await db.UserDevices.FirstOrDefaultAsync(ud => ud.UserId == userId && ud.DeviceId == endpoint.DeviceId, ct);
        if (link is null)
        {
            link = new UserDevice
            {
                UserId = userId,
                DeviceId = endpoint.DeviceId,
                Device = device,
                IsSyncOn = true,
                IsDeleted = false,
                LastModifiedAt = now
            };
            await db.UserDevices.AddAsync(link, ct);
        }
        else
        {
            link.VerifyIntegrity();
            link.Device = device;
            link.IsDeleted = false;
            link.DeletedAt = null;
            link.IsSyncOn = true;
            link.LastModifiedAt = now;
        }

        link.GenerateIntegrityHash();
        await EnsureLocalUserDeviceAsync(db, userId, ct);
        await db.SaveChangesAsync(ct);
    }




    private async Task CacheIncomingEnrollmentSourceEndpointAsync(
        IServiceProvider services,
        string sourceDeviceId,
        string sourceTlsCertFingerprint,
        string? sourceHost,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceHost))
            return;

        if (!Guid.TryParseExact(sourceDeviceId, "N", out var parsedDeviceId) &&
            !Guid.TryParse(sourceDeviceId, out parsedDeviceId))
            return;

        if (parsedDeviceId == Guid.Empty || parsedDeviceId == _identity.LocalDeviceId)
            return;

        if (string.IsNullOrWhiteSpace(sourceTlsCertFingerprint))
            return;

        if (IPAddress.TryParse(sourceHost, out var sourceAddress))
        {
            if (!IsUsableUnicastAddress(sourceAddress))
                return;
        }

        var db = services.GetRequiredService<AppDbContext>();
        var syncIdentities = services.GetRequiredService<ISyncDeviceIdentityService>();
        var syncTasks = services.GetRequiredService<IDeviceSyncTaskService>();

        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == parsedDeviceId, ct);
        if (device is null || !device.IsTrusted || device.IsBlocked)
            return;

        if (!string.Equals(NormalizeFingerprint(device.TlsCertFingerprint), NormalizeFingerprint(sourceTlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
            return;

        var endpoint = new DiscoveredDeviceEndpoint
        {
            Host = sourceHost.Trim(),
            Port = SyncPort,
            TlsCertFingerprint = sourceTlsCertFingerprint
        };

        _endpointCache.AddOrUpdate(endpoint);
        syncIdentities.TryAdd(device);
        syncTasks.TryStart(endpoint, device);
    }


    private async Task<DeviceEnrollmentSnapshot> BuildSnapshotAsync(IServiceProvider services, Guid userId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.Groups)
            .Include(u => u.UserDevices)
            .FirstOrDefaultAsync(u => u.UId == userId, ct);

        if (user is null)
            throw new UserNotFoundException();

        var allUserDevices = await db.UserDevices.AsNoTracking()
            .Where(ud => ud.UserId == userId)
            .ToListAsync(ct);
        foreach (var userDevice in allUserDevices)
            userDevice.VerifyIntegrity();

        var userDevices = allUserDevices.Where(ud => !ud.IsDeleted).ToList();
        var userDeviceIds = userDevices.Select(ud => ud.DeviceId).Distinct().ToList();

        var groups = await db.Groups.AsNoTracking()
            .Include(g => g.Users)
            .Where(g => g.Users.Any(u => u.UId == userId))
            .ToListAsync(ct);

        var devices = await db.Devices.AsNoTracking()
            .Include(d => d.UserDevices)
            .Where(d => d.Id != _identity.LocalDeviceId && userDeviceIds.Contains(d.Id))
            .ToListAsync(ct);

        foreach (var device in devices)
        {
            foreach (var link in device.UserDevices)
                link.VerifyIntegrity();
            device.GenerateIntegrityHash();
        }
        foreach (var group in groups)
            group.GenerateIntegrityHash();
        user.GenerateIntegrityHash();

        var deviceSnapshots = devices.Select(d => new DeviceEnrollmentDeviceSnapshot
        {
            Id = d.Id,
            PublicKey = d.PublicKey,
            SignPublicKey = d.SignPublicKey,
            TlsCertFingerprint = d.TlsCertFingerprint,
            DeviceType = d.DeviceType,
            LastKnownHash = d.LastKnownHash,
            LastSync = d.LastSync,
            LastSeen = d.LastSeen,
            IsTrusted = d.IsTrusted,
            IsBlocked = d.IsBlocked,
            BlockedReason = d.BlockedReason,
            BlockedAt = d.BlockedAt,
            InvalidSyncAttemptCount = d.InvalidSyncAttemptCount,
            LastInvalidSyncAttemptAt = d.LastInvalidSyncAttemptAt,
            LastModifiedAt = d.LastModifiedAt,
            IntegrityHash = d.IntegrityHash,
            UserIds = d.UserDevices.Where(ud => !ud.IsDeleted).Select(ud => ud.UserId).Distinct().ToList()
        }).ToList();

        var now = DateTimeOffset.UtcNow;
        var localDevice = new Device
        {
            Id = _identity.LocalDeviceId,
            PublicKey = _identity.AgreementPublicKey,
            SignPublicKey = _identity.SignPublicKey,
            TlsCertFingerprint = _identity.FingerprintHex,
            DeviceType = _identity.DeviceType,
            LastSync = now.UtcDateTime,
            LastSeen = now.UtcDateTime,
            IsTrusted = true,
            IsBlocked = false,
            LastModifiedAt = now
        };
        localDevice.GenerateIntegrityHash();
        var localUserDeviceSnapshotSource = new UserDevice
        {
            UserId = userId,
            DeviceId = _identity.LocalDeviceId,
            IsSyncOn = true,
            IsDeleted = false,
            LastModifiedAt = now
        };
        localUserDeviceSnapshotSource.GenerateIntegrityHash();

        deviceSnapshots.Add(new DeviceEnrollmentDeviceSnapshot
        {
            Id = localDevice.Id,
            PublicKey = localDevice.PublicKey,
            SignPublicKey = localDevice.SignPublicKey,
            TlsCertFingerprint = localDevice.TlsCertFingerprint,
            DeviceType = localDevice.DeviceType,
            LastKnownHash = localDevice.LastKnownHash,
            LastSync = localDevice.LastSync,
            LastSeen = localDevice.LastSeen,
            IsTrusted = true,
            IsBlocked = false,
            LastModifiedAt = localDevice.LastModifiedAt,
            IntegrityHash = localDevice.IntegrityHash,
            UserIds = [userId]
        });

        return new DeviceEnrollmentSnapshot
        {
            PrimaryUserId = user.UId,
            Users =
            [
                new DeviceEnrollmentUserSnapshot
                {
                    UId = user.UId,
                    UsernameHash = user.UsernameHash,
                    UsernameSalt = user.UsernameSalt,
                    PasswordSalt = user.PasswordSalt,
                    EncryptedPayload = user.EncryptedPayload,
                    LastModifiedAt = user.LastModifiedAt,
                    IntegrityHash = user.IntegrityHash,
                    GroupIds = user.Groups.Select(g => g.Id).Distinct().ToList()
                }
            ],
            Groups = groups.Select(g => new DeviceEnrollmentGroupSnapshot
            {
                Id = g.Id,
                EncryptedPayload = g.EncryptedPayload,
                LastModifiedAt = g.LastModifiedAt,
                IntegrityHash = g.IntegrityHash,
                UserIds = g.Users.Select(u => u.UId).Distinct().ToList()
            }).ToList(),
            Devices = deviceSnapshots,
            UserDevices = userDevices.Select(ud => new DeviceEnrollmentUserDeviceSnapshot
            {
                UserId = ud.UserId,
                DeviceId = ud.DeviceId,
                IsSyncOn = ud.IsSyncOn,
                IsDeleted = ud.IsDeleted,
                DeletedAt = ud.DeletedAt,
                LastModifiedAt = ud.LastModifiedAt,
                IntegrityHash = ud.IntegrityHash.ToArray()
            }).Append(new DeviceEnrollmentUserDeviceSnapshot
            {
                UserId = localUserDeviceSnapshotSource.UserId,
                DeviceId = localUserDeviceSnapshotSource.DeviceId,
                IsSyncOn = localUserDeviceSnapshotSource.IsSyncOn,
                IsDeleted = localUserDeviceSnapshotSource.IsDeleted,
                DeletedAt = localUserDeviceSnapshotSource.DeletedAt,
                LastModifiedAt = localUserDeviceSnapshotSource.LastModifiedAt,
                IntegrityHash = localUserDeviceSnapshotSource.IntegrityHash.ToArray()
            }).ToList()
        };
    }


    private async Task EnsureEncryptedDeviceDataAsync(IUserService users, User user, Guid token, Guid deviceId, CancellationToken ct)
    {
        using var userData = await users.GetAndVerifyUserDataAsync(user, token);
        if (userData.UserDevices.Devices.Any(device => device.Id == deviceId))
            return;

        var baseName = DeviceNameUtil.BuildDefaultDeviceName(deviceId);
        var name = BuildUniqueEncryptedDeviceName(userData, baseName, deviceId);
        var deviceData = new UserDeviceData
        {
            Id = deviceId,
            Name = name,
            LinkedAt = DateTimeOffset.UtcNow
        };
        deviceData.GenerateIntegrityHash();
        userData.UserDevices.Devices.Add(deviceData);
        userData.UserDevices.GenerateIntegrityHash();
        await users.UpdateUserDataAsync(userData, token, false, ct);
    }


    private static string BuildUniqueEncryptedDeviceName(UserData userData, string requestedName, Guid deviceId)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName)
            ? DeviceNameUtil.BuildDefaultDeviceName(deviceId)
            : requestedName.Trim();

        bool IsTaken(string value) => userData.UserDevices.Devices.Any(device =>
            device.Id != deviceId && string.Equals(device.Name, value, StringComparison.OrdinalIgnoreCase));

        if (!IsTaken(baseName))
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var suffix = $"-{i}";
            var prefixLength = Math.Min(baseName.Length, 64 - suffix.Length);
            var candidate = baseName[..prefixLength] + suffix;
            if (!IsTaken(candidate))
                return candidate;
        }

        throw new InvalidInputException();
    }


    private async Task<EnrollmentEndpoint> ResolveEndpointIdentityAsync(EnrollmentEndpoint endpoint, DeviceEnrollmentParsedCode parsed, CancellationToken ct)
    {
        DeviceEnrollmentTrace.Info($"Fetching and verifying enrollment identity from {endpoint.Host}:{endpoint.Port}.");
        var info = await FetchEnrollmentInfoAsync(endpoint, parsed, ct);
        if (!info.Ok)
        {
            DeviceEnrollmentTrace.Error($"Fetching enrollment identity from {endpoint.Host}:{endpoint.Port} failed with {info.ErrorCode}: {info.Error}");
            throw new DeviceEnrollmentException(info.ErrorCode, info.Error ?? "The new device did not return its enrollment identity.");
        }

        DeviceEnrollmentTrace.Info($"Fetched enrollment identity from {endpoint.Host}:{endpoint.Port}. DeviceId={info.DeviceId}, DeviceType={info.DeviceType}.");

        if (info.DeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(info.TlsCertFingerprint) ||
            info.SignPublicKey.Length != SyncConstants.SyncDeltaEd25519PublicKeyBytes ||
            info.AgreementPublicKey.Length != SyncConstants.SyncDeltaX25519PublicKeyBytes ||
            !DeviceTypeDetector.IsValid(info.DeviceType))
        {
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.NewDeviceRejected, "The new device returned incomplete or invalid enrollment identity data.");
        }

        if (endpoint.DeviceId != Guid.Empty && endpoint.DeviceId != info.DeviceId)
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.DeviceIdentityConflict, "The enrollment code device id does not match the responding device.");

        if (endpoint.SignPublicKey.Length > 0 && !endpoint.SignPublicKey.SequenceEqual(info.SignPublicKey))
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.DeviceIdentityConflict, "The enrollment code signing key does not match the responding device.");

        if (endpoint.AgreementPublicKey.Length > 0 && !endpoint.AgreementPublicKey.SequenceEqual(info.AgreementPublicKey))
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.DeviceIdentityConflict, "The enrollment code agreement key does not match the responding device.");

        var expectedFingerprint = NormalizeFingerprint(endpoint.TlsCertFingerprint);
        var actualFingerprint = NormalizeFingerprint(info.TlsCertFingerprint);
        if (expectedFingerprint.Length > 0 && !actualFingerprint.StartsWith(expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.DeviceIdentityConflict, "The enrollment code TLS fingerprint does not match the responding device.");

        var resolved = new EnrollmentEndpoint
        {
            Host = endpoint.Host,
            Port = endpoint.Port,
            DeviceId = info.DeviceId,
            TlsCertFingerprint = info.TlsCertFingerprint,
            SignPublicKey = info.SignPublicKey,
            AgreementPublicKey = info.AgreementPublicKey,
            DeviceType = info.DeviceType
        };

        if (IsLocalEndpoint(resolved))
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.NewDeviceRejected, "The enrollment code belongs to this local device.");

        return resolved;
    }


    private async Task<DeviceEnrollmentInfoResponse> FetchEnrollmentInfoAsync(EnrollmentEndpoint endpoint, DeviceEnrollmentParsedCode parsed, CancellationToken ct)
    {
        try
        {
            var reply = await _syncTransport.GetDeviceEnrollmentInfoAsync(endpoint.Host, endpoint.Port, endpoint.TlsCertFingerprint, new GetDeviceEnrollmentInfoRequest
            {
                SessionId = parsed.SessionId,
                CodeProof = ByteString.CopyFrom(DeviceEnrollmentCode.BuildEnrollmentInfoProof(parsed.SessionId, parsed.Secret))
            }, ct);

            var errorCode = Enum.TryParse<DeviceEnrollmentErrorCode>(reply.ErrorCode, out var parsedErrorCode)
                ? parsedErrorCode
                : DeviceEnrollmentErrorCode.NewDeviceRejected;

            if (!reply.Ok)
            {
                return new DeviceEnrollmentInfoResponse
                {
                    Ok = false,
                    ErrorCode = errorCode,
                    Error = string.IsNullOrWhiteSpace(reply.Error) ? "The new device rejected the enrollment identity request." : reply.Error
                };
            }

            if (!Guid.TryParseExact(reply.DeviceId, "N", out var deviceId) && !Guid.TryParse(reply.DeviceId, out deviceId))
            {
                return new DeviceEnrollmentInfoResponse
                {
                    Ok = false,
                    ErrorCode = DeviceEnrollmentErrorCode.NewDeviceRejected,
                    Error = "The new device returned an invalid device id."
                };
            }

            if (reply.DeviceType > byte.MaxValue || !DeviceTypeDetector.IsValid((DeviceType)(byte)reply.DeviceType))
            {
                return new DeviceEnrollmentInfoResponse
                {
                    Ok = false,
                    ErrorCode = DeviceEnrollmentErrorCode.NewDeviceRejected,
                    Error = "The new device returned an invalid device type."
                };
            }

            return new DeviceEnrollmentInfoResponse
            {
                Ok = true,
                DeviceId = deviceId,
                DeviceType = (DeviceType)(byte)reply.DeviceType,
                TlsCertFingerprint = reply.TlsCertFingerprint,
                SignPublicKey = reply.SignPub.ToByteArray(),
                AgreementPublicKey = reply.AgreementPub.ToByteArray()
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or CryptographicException or ArgumentException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            return new DeviceEnrollmentInfoResponse
            {
                Ok = false,
                ErrorCode = DeviceEnrollmentErrorCode.NewDeviceConnectionFailed,
                Error = ex.Message
            };
        }
    }


    private async Task<(bool Ok, DeviceEnrollmentErrorCode ErrorCode, string? Error)> SendEnrollmentSnapshotAsync(EnrollmentEndpoint endpoint, string sessionId, byte[] secret, byte[] proof, DeviceEnrollmentSnapshot snapshot, CancellationToken ct)
    {
        byte[] snapshotBytes;
        try
        {
            snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            DeviceEnrollmentTrace.Error($"Could not serialize enrollment snapshot for {endpoint.Host}:{endpoint.Port}: {ex.Message}", ex);
            return (false, DeviceEnrollmentErrorCode.ProfileDataInvalid, ex.Message);
        }

        DeviceEnrollmentTrace.Info($"Enrollment snapshot prepared for {endpoint.Host}:{endpoint.Port}. Size={snapshotBytes.Length} bytes.");

        if (snapshotBytes.Length > SyncConstants.MaxDeviceEnrollmentSnapshotBytes)
            return (false, DeviceEnrollmentErrorCode.ProfileDataTooLarge, "The profile data is too large to transfer in one enrollment request.");

        byte[] encryptedSnapshotBytes;
        byte[] snapshotNonce;
        byte[] snapshotTag;
        try
        {
            (encryptedSnapshotBytes, snapshotNonce, snapshotTag) = EncryptEnrollmentSnapshot(
                sessionId,
                secret,
                snapshotBytes,
                _identity.LocalDeviceId.ToString("N"),
                _identity.SignPublicKey,
                _identity.FingerprintHex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(snapshotBytes);
        }

        using var transferTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        transferTimeout.CancelAfter(TimeSpan.FromSeconds(SyncConstants.DeviceEnrollmentTransferTimeoutSeconds));

        try
        {
            DeviceEnrollmentTrace.Info($"Trying TCP enrollment snapshot transfer to {endpoint.Host}:{endpoint.Port}.");
            var result = await SendEnrollmentSnapshotStreamAsync(endpoint, sessionId, proof, encryptedSnapshotBytes, snapshotNonce, snapshotTag, transferTimeout.Token);

            if (result.Ok)
                DeviceEnrollmentTrace.Info($"TCP enrollment snapshot transfer to {endpoint.Host}:{endpoint.Port} completed successfully.");
            else
                DeviceEnrollmentTrace.Error($"TCP enrollment snapshot transfer to {endpoint.Host}:{endpoint.Port} returned {result.ErrorCode}: {result.Error}");

            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or CryptographicException or ArgumentException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            DeviceEnrollmentTrace.Error($"TCP enrollment snapshot transfer to {endpoint.Host}:{endpoint.Port} threw: {ex.Message}", ex);
            return (false, DeviceEnrollmentErrorCode.NewDeviceConnectionFailed, ex.Message);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptedSnapshotBytes);
            CryptographicOperations.ZeroMemory(snapshotNonce);
            CryptographicOperations.ZeroMemory(snapshotTag);
        }
    }


    private async Task<(bool Ok, DeviceEnrollmentErrorCode ErrorCode, string? Error)> SendEnrollmentSnapshotStreamAsync(EnrollmentEndpoint endpoint, string sessionId, byte[] proof, byte[] snapshotBytes, byte[] snapshotNonce, byte[] snapshotTag, CancellationToken ct)
    {
        var reply = await _syncTransport.CompleteDeviceEnrollmentStreamAsync(
            endpoint.Host,
            endpoint.Port,
            endpoint.TlsCertFingerprint,
            BuildEnrollmentSnapshotChunks(sessionId, proof, snapshotBytes, snapshotNonce, snapshotTag),
            ct);

        return ParseEnrollmentReply(reply);
    }


    private async IAsyncEnumerable<CompleteDeviceEnrollmentChunk> BuildEnrollmentSnapshotChunks(string sessionId, byte[] proof, byte[] snapshotBytes, byte[] snapshotNonce, byte[] snapshotTag)
    {
        var sourceDeviceId = _identity.LocalDeviceId.ToString("N");

        for (var offset = 0; offset < snapshotBytes.Length; offset += SyncConstants.DeviceEnrollmentSnapshotChunkBytes)
        {
            var count = Math.Min(SyncConstants.DeviceEnrollmentSnapshotChunkBytes, snapshotBytes.Length - offset);
            yield return new CompleteDeviceEnrollmentChunk
            {
                SessionId = offset == 0 ? sessionId : string.Empty,
                CodeProof = offset == 0 ? ByteString.CopyFrom(proof) : ByteString.Empty,
                SourceDeviceId = offset == 0 ? sourceDeviceId : string.Empty,
                SourceSignPub = offset == 0 ? ByteString.CopyFrom(_identity.SignPublicKey) : ByteString.Empty,
                SourceTlsCertFingerprint = offset == 0 ? _identity.FingerprintHex : string.Empty,
                SnapshotEncryptionVersion = offset == 0 ? SyncConstants.EnrollmentSnapshotEncryptionVersion : 0,
                SnapshotEncryptionNonce = offset == 0 ? ByteString.CopyFrom(snapshotNonce) : ByteString.Empty,
                SnapshotEncryptionTag = offset == 0 ? ByteString.CopyFrom(snapshotTag) : ByteString.Empty,
                SnapshotChunk = ByteString.CopyFrom(snapshotBytes, offset, count)
            };

            await Task.Yield();
        }

        if (snapshotBytes.Length == 0)
        {
            yield return new CompleteDeviceEnrollmentChunk
            {
                SessionId = sessionId,
                CodeProof = ByteString.CopyFrom(proof),
                SourceDeviceId = sourceDeviceId,
                SourceSignPub = ByteString.CopyFrom(_identity.SignPublicKey),
                SourceTlsCertFingerprint = _identity.FingerprintHex,
                SnapshotEncryptionVersion = SyncConstants.EnrollmentSnapshotEncryptionVersion,
                SnapshotEncryptionNonce = ByteString.CopyFrom(snapshotNonce),
                SnapshotEncryptionTag = ByteString.CopyFrom(snapshotTag),
                SnapshotChunk = ByteString.Empty
            };
        }
    }


    private static (bool Ok, DeviceEnrollmentErrorCode ErrorCode, string? Error) ParseEnrollmentReply(CompleteDeviceEnrollmentReply reply)
    {
        if (reply.Ok)
            return (true, DeviceEnrollmentErrorCode.Unknown, null);

        var errorCode = Enum.TryParse<DeviceEnrollmentErrorCode>(reply.ErrorCode, out var parsedErrorCode)
            ? parsedErrorCode
            : DeviceEnrollmentErrorCode.NewDeviceRejected;

        return (false, errorCode, string.IsNullOrWhiteSpace(reply.Error) ? "The new device rejected the enrollment request." : reply.Error);
    }


    private static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptEnrollmentSnapshot(
        string sessionId,
        byte[] secret,
        byte[] plaintext,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsFingerprint)
    {
        var key = DeviceEnrollmentCode.BuildSnapshotEncryptionKey(sessionId, secret);
        var nonce = RandomNumberGenerator.GetBytes(SyncConstants.EnrollmentSnapshotEncryptionNonceBytes);
        var tag = new byte[SyncConstants.EnrollmentSnapshotEncryptionTagBytes];
        var ciphertext = new byte[plaintext.Length];
        var aad = DeviceEnrollmentCode.BuildSnapshotEncryptionAad(sessionId, sourceDeviceId, sourceSignPublicKey, sourceTlsFingerprint);

        using var aes = new AesGcm(key, SyncConstants.EnrollmentSnapshotEncryptionTagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        CryptographicOperations.ZeroMemory(key);
        return (ciphertext, nonce, tag);
    }


    private static byte[] DecryptEnrollmentSnapshot(
        string sessionId,
        byte[] secret,
        byte[] ciphertext,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsFingerprint,
        int encryptionVersion,
        byte[] nonce,
        byte[] tag)
    {
        if (encryptionVersion != SyncConstants.EnrollmentSnapshotEncryptionVersion)
            throw new InvalidDataException("The enrollment snapshot encryption version is invalid.");

        if (nonce.Length != SyncConstants.EnrollmentSnapshotEncryptionNonceBytes)
            throw new InvalidDataException("The enrollment snapshot encryption nonce is invalid.");

        if (tag.Length != SyncConstants.EnrollmentSnapshotEncryptionTagBytes)
            throw new InvalidDataException("The enrollment snapshot authentication tag is invalid.");

        var key = DeviceEnrollmentCode.BuildSnapshotEncryptionKey(sessionId, secret);
        var plaintext = new byte[ciphertext.Length];
        var aad = DeviceEnrollmentCode.BuildSnapshotEncryptionAad(sessionId, sourceDeviceId, sourceSignPublicKey, sourceTlsFingerprint);

        try
        {
            using var aes = new AesGcm(key, SyncConstants.EnrollmentSnapshotEncryptionTagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }


    private static void RejectSensitiveLocalOnlySnapshotPayload(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            foreach (var propertyName in SensitiveLocalOnlySnapshotPropertyNames)
            {
                if (ContainsProperty(doc.RootElement, propertyName))
                    throw new InvalidDataException("Enrollment snapshot contains local-only device or key material.");
            }
        }
        catch (JsonException)
        {
            throw;
        }
    }


    private static readonly string[] SensitiveLocalOnlySnapshotPropertyNames =
    [
        "SavedKey",
        "LocalDeviceIdentity",
        "LocalUserDevice",
        "LocalUserDevices",
        "DeviceIdentity",
        "AgreementPrivateKeyBlob",
        "SignPrivateKeyBlob",
        "PFXCertificate",
        "PrivateKey",
        "PrivateKeyBlob"
    ];


    private static bool ContainsProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (ContainsProperty(property.Value, propertyName))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsProperty(item, propertyName))
                    return true;
            }
        }

        return false;
    }


    private async Task ImportSnapshotAsync(IServiceProvider services, DeviceEnrollmentSnapshot snapshot, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var syncIdentities = services.GetRequiredService<ISyncDeviceIdentityService>();
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await RemoveLocalDeviceRowsAsync(db, ct);

        foreach (var deviceSnapshot in snapshot.Devices)
        {
            if (!DeviceTypeDetector.IsValid(deviceSnapshot.DeviceType))
                throw new InvalidDataException("The enrollment snapshot contains an invalid device type.");

            var isLocalDevice = deviceSnapshot.Id == _identity.LocalDeviceId ||
                deviceSnapshot.SignPublicKey.SequenceEqual(_identity.SignPublicKey) ||
                string.Equals(NormalizeFingerprint(deviceSnapshot.TlsCertFingerprint), NormalizeFingerprint(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase);

            if (isLocalDevice)
                continue;

            var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceSnapshot.Id, ct);
            if (device is null)
            {
                device = new Device { Id = deviceSnapshot.Id };
                await db.Devices.AddAsync(device, ct);
            }

            device.PublicKey = deviceSnapshot.PublicKey;
            device.SignPublicKey = deviceSnapshot.SignPublicKey;
            device.TlsCertFingerprint = deviceSnapshot.TlsCertFingerprint;
            device.DeviceType = deviceSnapshot.DeviceType;
            device.LastKnownHash = deviceSnapshot.LastKnownHash;
            device.LastSync = deviceSnapshot.LastSync;
            device.LastSeen = deviceSnapshot.LastSeen;
            device.IsTrusted = true;
            device.IsBlocked = deviceSnapshot.IsBlocked;
            device.BlockedReason = deviceSnapshot.BlockedReason;
            device.BlockedAt = deviceSnapshot.BlockedAt;
            device.InvalidSyncAttemptCount = deviceSnapshot.InvalidSyncAttemptCount;
            device.LastInvalidSyncAttemptAt = deviceSnapshot.LastInvalidSyncAttemptAt;
            device.LastModifiedAt = deviceSnapshot.LastModifiedAt == default ? now : deviceSnapshot.LastModifiedAt;
            device.GenerateIntegrityHash();
        }

        await db.SaveChangesAsync(ct);

        foreach (var userSnapshot in snapshot.Users)
        {
            var user = await db.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.UId == userSnapshot.UId, ct);
            if (user is null)
            {
                user = new User { UId = userSnapshot.UId };
                await db.Users.AddAsync(user, ct);
            }

            user.UsernameHash = userSnapshot.UsernameHash;
            user.UsernameSalt = userSnapshot.UsernameSalt;
            user.PasswordSalt = userSnapshot.PasswordSalt;
            user.EncryptedPayload = userSnapshot.EncryptedPayload;
            user.SavedKey = null;
            user.LastModifiedAt = userSnapshot.LastModifiedAt == default ? now : userSnapshot.LastModifiedAt;
            user.IntegrityHash = userSnapshot.IntegrityHash;
        }

        foreach (var groupSnapshot in snapshot.Groups)
        {
            var group = await db.Groups.Include(g => g.Users).FirstOrDefaultAsync(g => g.Id == groupSnapshot.Id, ct);
            if (group is null)
            {
                group = new Group { Id = groupSnapshot.Id };
                await db.Groups.AddAsync(group, ct);
            }

            group.EncryptedPayload = groupSnapshot.EncryptedPayload;
            group.LastModifiedAt = groupSnapshot.LastModifiedAt == default ? now : groupSnapshot.LastModifiedAt;
            group.IntegrityHash = groupSnapshot.IntegrityHash;
        }

        await db.SaveChangesAsync(ct);

        foreach (var groupSnapshot in snapshot.Groups)
        {
            var group = await db.Groups.Include(g => g.Users).FirstAsync(g => g.Id == groupSnapshot.Id, ct);
            var userIds = groupSnapshot.UserIds.Where(id => id != Guid.Empty).Distinct().ToHashSet();

            foreach (var user in group.Users.Where(u => !userIds.Contains(u.UId)).ToList())
                group.Users.Remove(user);

            foreach (var userId in userIds)
            {
                if (group.Users.Any(u => u.UId == userId))
                    continue;

                var user = await db.Users.FirstOrDefaultAsync(u => u.UId == userId, ct);
                if (user is not null)
                    group.Users.Add(user);
            }
        }

        var linkSnapshots = snapshot.UserDevices
            .Where(ud => ud.UserId != Guid.Empty && ud.DeviceId != Guid.Empty)
            .GroupBy(ud => new { ud.UserId, ud.DeviceId })
            .Select(group => group.OrderByDescending(ud => ud.LastModifiedAt).First())
            .ToList();

        foreach (var linkSnapshot in linkSnapshots)
        {
            if (linkSnapshot.LastModifiedAt == default || linkSnapshot.IntegrityHash.Length == 0)
                throw new InvalidDataException("The enrollment snapshot contains an incomplete user-device relationship.");

            var verifiedSnapshotLink = new UserDevice
            {
                UserId = linkSnapshot.UserId,
                DeviceId = linkSnapshot.DeviceId,
                IsSyncOn = linkSnapshot.IsSyncOn,
                IsDeleted = linkSnapshot.IsDeleted,
                DeletedAt = linkSnapshot.DeletedAt,
                LastModifiedAt = linkSnapshot.LastModifiedAt
            };
            verifiedSnapshotLink.GenerateIntegrityHash();
            if (!Hashing.Verify(linkSnapshot.IntegrityHash, verifiedSnapshotLink.IntegrityHash))
                throw new InvalidDataException("The enrollment snapshot contains an invalid user-device relationship hash.");

            if (linkSnapshot.DeviceId == _identity.LocalDeviceId)
                continue;

            var remoteDevice = await db.Devices.FirstOrDefaultAsync(d => d.Id == linkSnapshot.DeviceId, ct);
            if (remoteDevice is null)
                continue;

            var link = await GetOrCreateTrackedUserDeviceAsync(db, linkSnapshot.UserId, linkSnapshot.DeviceId, ct);
            link.Device = remoteDevice;
            link.IsSyncOn = linkSnapshot.IsSyncOn;
            link.IsDeleted = linkSnapshot.IsDeleted;
            link.DeletedAt = linkSnapshot.DeletedAt;
            link.LastModifiedAt = linkSnapshot.LastModifiedAt;
            link.IntegrityHash = verifiedSnapshotLink.IntegrityHash.ToArray();
        }

        await EnsureLocalUserDeviceAsync(db, snapshot.PrimaryUserId, ct);

        await db.SaveChangesAsync(ct);
        await RemoveLocalDeviceRowsAsync(db, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var trustedDevices = await db.Devices.AsNoTracking()
            .Where(d => d.IsTrusted && !d.IsBlocked)
            .ToListAsync(ct);

        foreach (var device in trustedDevices)
            syncIdentities.TryAdd(device);
    }


    private static async Task<UserDevice> GetOrCreateTrackedUserDeviceAsync(AppDbContext db, Guid userId, Guid deviceId, CancellationToken ct)
    {
        var tracked = db.ChangeTracker.Entries<UserDevice>()
            .Where(e => e.State != EntityState.Deleted && e.State != EntityState.Detached)
            .Select(e => e.Entity)
            .FirstOrDefault(ud => ud.UserId == userId && ud.DeviceId == deviceId);

        if (tracked is not null)
            return tracked;

        var existing = await db.UserDevices.FirstOrDefaultAsync(ud => ud.UserId == userId && ud.DeviceId == deviceId, ct);
        if (existing is not null)
        {
            existing.VerifyIntegrity();
            return existing;
        }

        var created = new UserDevice
        {
            UserId = userId,
            DeviceId = deviceId
        };

        await db.UserDevices.AddAsync(created, ct);
        return created;
    }


    private async Task RemoveLocalDeviceRowsAsync(AppDbContext db, CancellationToken ct)
    {
        var devices = await db.Devices.ToListAsync(ct);
        var localRows = devices.Where(device =>
            device.Id == _identity.LocalDeviceId ||
            device.SignPublicKey.SequenceEqual(_identity.SignPublicKey) ||
            string.Equals(NormalizeFingerprint(device.TlsCertFingerprint), NormalizeFingerprint(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (localRows.Count != 0)
            db.Devices.RemoveRange(localRows);
    }


    private async Task EnsureLocalUserDeviceAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        await RemoveLocalDeviceRowsAsync(db, ct);
        var link = await db.LocalUserDevices.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (link is null)
        {
            var localUserDevice = new LocalUserDevice
            {
                UserId = userId,
                LocalDeviceIdentityId = _identity.LocalDeviceId,
                IsSyncOn = true
            };
            localUserDevice.GenerateIntegrityHash();
            await db.LocalUserDevices.AddAsync(localUserDevice, ct);
            return;
        }
        link.VerifyIntegrity();
        link.LocalDeviceIdentityId = _identity.LocalDeviceId;
        link.GenerateIntegrityHash();
    }


    private async Task QueueInitialSyncAsync(IServiceProvider services, Guid userId, Guid newDeviceId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var syncQueue = services.GetRequiredService<ISyncQueueService>();

        var groupIds = await db.Groups.AsNoTracking()
            .Where(g => g.Users.Any(u => u.UId == userId))
            .Select(g => g.Id)
            .ToListAsync(ct);

        await syncQueue.EnqueueAsync(new SyncItem { ModelId = userId, ModelType = SyncModelType.User, ChangeType = SyncChangeType.Updated }, ct);

        foreach (var groupId in groupIds)
            await syncQueue.EnqueueAsync(new SyncItem { ModelId = groupId, ModelType = SyncModelType.Group, ChangeType = SyncChangeType.Updated }, ct);

        await syncQueue.EnqueueAsync(new SyncItem { ModelId = newDeviceId, ModelType = SyncModelType.Device, ChangeType = SyncChangeType.Created }, ct);
        await syncQueue.EnqueueAsync(new SyncItem { ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userId, newDeviceId), ModelType = SyncModelType.UserDevice, ChangeType = SyncChangeType.Created }, ct);
    }


    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();


    public void Dispose()
    {
        lock (_lock)
        {
            CancelEnrollmentExpirationLocked();
            StopAdvertisingLocked();
            _currentSession = null;
        }
    }
}
