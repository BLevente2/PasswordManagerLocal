using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Constants.SyncConstants;
using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.State;
using PasswordManagerLocal.Backend.Sync.Enrollment;

namespace PasswordManagerLocal.Backend.Services;

public sealed class DeviceEnrollmentService : IDeviceEnrollmentService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _identity;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly ISyncTransportClientService _syncTransport;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly ILocalDiscoveryService _localDiscovery;
    private readonly ILocalNetworkAddressService _networkAddresses;
    private readonly object _lock = new();
    private EnrollmentSession? _currentSession;
    private CancellationTokenSource? _enrollmentExpirationCancellation;

    public DeviceEnrollmentService(
        IServiceScopeFactory scopeFactory,
        IDeviceIdentityService identity,
        IDiscoveredDeviceEndpointCache endpointCache,
        ISyncTransportClientService syncTransport,
        ISyncRuntimeService syncRuntime,
        ILocalDiscoveryService localDiscovery,
        ILocalNetworkAddressService networkAddresses)
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
        _endpointCache = endpointCache;
        _syncTransport = syncTransport;
        _syncRuntime = syncRuntime;
        _localDiscovery = localDiscovery;
        _networkAddresses = networkAddresses;
    }




    public async Task<DeviceEnrollmentCodeResponse> StartEnrollmentAsync(CancellationToken ct = default)
    {
        await _syncRuntime.BeginEnrollmentOnlyAsync(ct);
        EnrollmentSession? session = null;

        try
        {
            DeviceEnrollmentDirectEndpointInfo directEndpointInfo;

            lock (_lock)
            {
                CancelEnrollmentExpirationLocked();
                DeactivateEnrollmentDiscoveryLocked();
                _currentSession?.ClearSensitiveData();
                directEndpointInfo = BuildDirectEndpointInfo();
                var generated = DeviceEnrollmentCode.Create(directEndpointInfo);
                session = new EnrollmentSession
                {
                    SessionId = generated.SessionId,
                    Secret = generated.Secret,
                    Code = generated.Code,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                    State = DeviceEnrollmentState.Waiting
                };
                _currentSession = session;
                _localDiscovery.ActivateEnrollmentSession(session.SessionId, session.Secret, session.ExpiresAt);
                StartEnrollmentExpirationCountdownLocked(session);
            }

            await VerifyLocalEnrollmentListenerAsync(session, directEndpointInfo, ct);
            return new DeviceEnrollmentCodeResponse { Code = session.Code, ExpiresAt = session.ExpiresAt };
        }
        catch
        {
            lock (_lock)
            {
                if (session is not null && ReferenceEquals(_currentSession, session))
                {
                    CancelEnrollmentExpirationLocked();
                    DeactivateEnrollmentDiscoveryLocked();
                    session.ClearSensitiveData();
                    _currentSession = null;
                }
            }

            await _syncRuntime.EndEnrollmentOnlyAsync(CancellationToken.None);
            throw;
        }
    }


    private async Task VerifyLocalEnrollmentListenerAsync(EnrollmentSession session, DeviceEnrollmentDirectEndpointInfo endpointInfo, CancellationToken ct)
    {
        var hosts = endpointInfo.Hosts
            .Where(host => !string.IsNullOrWhiteSpace(host) && IPAddress.TryParse(host, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hosts.Count == 0)
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.LocalNetworkUnavailable, "No usable local network address was available after checking active interfaces, DNS, and the platform route fallback.");

        var failures = new List<string>();
        var request = new GetDeviceEnrollmentInfoRequest
        {
            SessionId = session.SessionId,
            CodeProof = ByteString.CopyFrom(DeviceEnrollmentCode.BuildEnrollmentInfoProof(session.SessionId, session.Secret)),
            SourceDatabaseVersion = DatabaseConstants.CurrentDbVersion
        };

        foreach (var host in hosts)
        {
            try
            {
                DeviceEnrollmentTrace.Info($"Local enrollment listener self-test started for {host}:{endpointInfo.Port}.");
                var reply = await _syncTransport.GetDeviceEnrollmentInfoAsync(
                    host,
                    endpointInfo.Port,
                    endpointInfo.TlsCertFingerprint,
                    request,
                    ct);

                if (reply.Ok &&
                    Guid.TryParse(reply.DeviceId, out var deviceId) &&
                    deviceId == _identity.LocalDeviceId &&
                    FingerprintUtil.Normalize(reply.TlsCertFingerprint) == FingerprintUtil.Normalize(_identity.FingerprintHex))
                {
                    DeviceEnrollmentTrace.Info($"Local enrollment listener self-test succeeded for {host}:{endpointInfo.Port}.");
                    return;
                }

                var error = string.IsNullOrWhiteSpace(reply.Error) ? "The listener returned an invalid local identity." : reply.Error;
                failures.Add($"{host}:{endpointInfo.Port} -> {error}");
                DeviceEnrollmentTrace.Error($"Local enrollment listener self-test failed for {host}:{endpointInfo.Port}: {error}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException or InvalidOperationException or CryptographicException or ArgumentException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
            {
                failures.Add($"{host}:{endpointInfo.Port} -> {ex.Message}");
                DeviceEnrollmentTrace.Error($"Local enrollment listener self-test failed for {host}:{endpointInfo.Port}: {ex.Message}", ex);
            }
        }

        throw new DeviceEnrollmentException(
            DeviceEnrollmentErrorCode.LocalEnrollmentListenerUnavailable,
            $"The local TCP enrollment listener could not complete its own authenticated self-test. {string.Join("; ", failures)}");
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
            DeactivateEnrollmentDiscoveryLocked();
            _currentSession?.ClearSensitiveData();
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

        RejectLocalDeviceEnrollmentCode(parsed);

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
        var discoveryFailures = new List<string>();
        var attemptedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DeviceEnrollmentTrace.Info($"AddDeviceByCode started. Session={parsed.SessionId}, directEndpointCount={directEndpoints.Count}, authoritativeDirect={hasAuthoritativeSameSubnetDirectEndpoint}, directEndpointCandidates={string.Join(", ", directEndpointCandidates.Select(e => $"{e.Endpoint.Host}:{e.Endpoint.Port}/priority={e.Priority}"))}");

        foreach (var endpoint in directEndpoints)
        {
            try
            {
                attemptedEndpoints.Add($"{endpoint.Host}:{endpoint.Port}");
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
                ? "The same-subnet direct endpoint did not respond. Trying authenticated local discovery fallback because Wi-Fi/wired bridges can still expose another usable address."
                : "The direct enrollment endpoints did not respond. Trying authenticated local discovery fallback.";

            DeviceEnrollmentTrace.Info(reason);
        }

        IReadOnlyList<EnrollmentEndpoint> discoveryEndpoints;
        try
        {
            DeviceEnrollmentTrace.Info("Trying authenticated local enrollment discovery fallback.");
            discoveryEndpoints = await _localDiscovery.FindEnrollmentEndpointsAsync(parsed, ct);
        }
        catch (DeviceEnrollmentException ex) when (directFailures.Count > 0)
        {
            throw new DeviceEnrollmentException(
                DeviceEnrollmentErrorCode.NewDeviceConnectionFailed,
                BuildEndpointFailureMessage(
                    BuildLocalDiscoveryFailedMessage(hasAuthoritativeSameSubnetDirectEndpoint),
                    directFailures),
                ex);
        }

        foreach (var endpoint in discoveryEndpoints)
        {
            var endpointKey = $"{endpoint.Host}:{endpoint.Port}";
            var isAuthenticatedRediscoveryRetry = !attemptedEndpoints.Add(endpointKey);

            if (isAuthenticatedRediscoveryRetry)
            {
                DeviceEnrollmentTrace.Info($"Retrying enrollment endpoint {endpointKey} because a fresh authenticated local discovery response confirmed that the same enrollment session is still active there.");
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            }

            try
            {
                var attemptKind = isAuthenticatedRediscoveryRetry ? "authenticated-discovery-confirmed retry" : "authenticated local discovery endpoint";
                DeviceEnrollmentTrace.Info($"Trying {attemptKind} {endpoint.Host}:{endpoint.Port}.");
                await CompleteEnrollmentWithEndpointAsync(token, parsed, endpoint, ct);
                DeviceEnrollmentTrace.Info($"{attemptKind} {endpoint.Host}:{endpoint.Port} completed successfully.");
                return;
            }
            catch (DeviceEnrollmentException ex) when (ex.ErrorCode == DeviceEnrollmentErrorCode.NewDeviceConnectionFailed)
            {
                var attemptKind = isAuthenticatedRediscoveryRetry ? "authenticated-discovery-confirmed retry" : "authenticated local discovery";
                discoveryFailures.Add($"{endpoint.Host}:{endpoint.Port} ({attemptKind}) -> {ex.Message}");
                DeviceEnrollmentTrace.Error($"{attemptKind} enrollment endpoint {endpoint.Host}:{endpoint.Port} failed with a connection/transfer error: {ex.Message}", ex);
            }
        }

        throw new DeviceEnrollmentException(
            DeviceEnrollmentErrorCode.NewDeviceConnectionFailed,
            BuildEndpointFailureMessage(
                BuildNoEndpointAcceptedMessage(hasAuthoritativeSameSubnetDirectEndpoint, discoveryEndpoints.Count > 0),
                directFailures.Concat(discoveryFailures)));
    }


    private string BuildEndpointFailureMessage(string message, IEnumerable<string> endpointFailures)
    {
        var failures = endpointFailures
            .Where(failure => !string.IsNullOrWhiteSpace(failure))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (failures.Count == 0)
            return message;

        return $"{message} Attempted endpoints: {string.Join("; ", failures)}";
    }


    private string BuildNoEndpointAcceptedMessage(bool hadSameSubnetDirectEndpoint, bool authenticatedDiscoveryResponseReceived)
    {
        if (hadSameSubnetDirectEndpoint && authenticatedDiscoveryResponseReceived)
        {
            return "The enrollment code contained a same-subnet address and the old device received a fresh authenticated local discovery response from that enrollment session, but TCP port 26688 still did not complete a connection. The app retried the local discovery-confirmed address. The failure happened before device identity verification or profile transfer, so the remaining problem is in the TCP-specific path between the two Windows devices, such as effective OS filtering, adapter routing, a transient Wi-Fi/ARP state, or a duplicate IP address.";
        }

        if (hadSameSubnetDirectEndpoint)
        {
            return "The enrollment code contained a same-subnet address for the new device, but no known or authenticated-discovery address completed the authenticated TCP enrollment connection. The failure happened before device identity verification or profile transfer. Keep the enrollment screen open and verify that the target app is still listening on TCP port 26688 and that TCP traffic reaches that process.";
        }

        return "The new device was discovered, but none of the reachable network addresses accepted the enrollment transfer.";
    }


    private string BuildLocalDiscoveryFailedMessage(bool hadSameSubnetDirectEndpoint)
    {
        if (hadSameSubnetDirectEndpoint)
        {
            return "The enrollment code contained a same-subnet address for the new device, but the authenticated TCP enrollment connection could not be completed and authenticated local discovery also failed. Keep the enrollment screen open and verify that the target app is still listening on TCP port 26688 and that inbound traffic reaches that process.";
        }

        return "The new device was included in the enrollment code, but it could not be reached directly and authenticated local discovery also failed.";
    }


    private async Task CompleteEnrollmentWithEndpointAsync(Guid token, DeviceEnrollmentParsedCode parsed, EnrollmentEndpoint endpoint, CancellationToken ct)
    {
        DeviceEnrollmentTrace.Info($"Enrollment connection and identity check started for {endpoint.Host}:{endpoint.Port}. HasEmbeddedIdentity={endpoint.DeviceId != Guid.Empty}.");
        endpoint = await ResolveEndpointIdentityAsync(endpoint, parsed, ct);
        DeviceEnrollmentTrace.Info($"Enrollment identity resolved for {endpoint.Host}:{endpoint.Port}. DeviceId={endpoint.DeviceId}, TlsFingerprintPrefix={FingerprintUtil.Normalize(endpoint.TlsCertFingerprint)[..Math.Min(16, FingerprintUtil.Normalize(endpoint.TlsCertFingerprint).Length)]}.");

        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        var cache = scope.ServiceProvider.GetRequiredService<IDataCachingService>();
        var syncIdentities = scope.ServiceProvider.GetRequiredService<ISyncDeviceIdentityService>();
        var syncTasks = scope.ServiceProvider.GetRequiredService<IDeviceSyncTaskService>();
        var user = await users.GetAndVerifyUserAsync(token, ct);

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

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

            var remoteDevice = await devices.GetByIdAsNoTrackingAsync(endpoint.DeviceId, ct);
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

        if (!string.Equals(FingerprintUtil.Normalize(sourceTlsCertFingerprint), FingerprintUtil.Normalize(actualClientTlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
        {
            DeviceEnrollmentTrace.Error($"Incoming enrollment rejected because client TLS fingerprint did not match. Expected={FingerprintUtil.Normalize(sourceTlsCertFingerprint)}, Actual={FingerprintUtil.Normalize(actualClientTlsCertFingerprint)}.");
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
            snapshot = JsonSerializer.Deserialize<DeviceEnrollmentSnapshot>(plaintextSnapshotBytes, DataCodec.JsonOpts);
            if (snapshot is not null)
                UtcDateTimeUtil.NormalizeObjectGraph(snapshot);
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
            await RejectIfPrimaryUserAlreadyLinkedToLocalDeviceAsync(scope.ServiceProvider, snapshot.PrimaryUserId, ct);
            await ImportSnapshotAsync(scope.ServiceProvider, snapshot, ct);
            await _syncRuntime.RefreshSyncEnabledAsync(ct);
            await CacheIncomingEnrollmentSourceEndpointAsync(scope.ServiceProvider, sourceDeviceId, sourceTlsCertFingerprint, sourceHost, ct);
            DeviceEnrollmentTrace.Info("Incoming enrollment snapshot import completed successfully.");
        }
        catch (DeviceEnrollmentException ex)
        {
            DeviceEnrollmentTrace.Error($"Incoming enrollment snapshot import rejected: {ex.Message}", ex);
            return await RejectIncomingValidationAsync(ex.ErrorCode, ex.Message);
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
                DeactivateEnrollmentDiscoveryLocked();
                _currentSession.ClearSensitiveData();
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
            DeactivateEnrollmentDiscoveryLocked();
            session.ClearSensitiveData();
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
            Hosts = _networkAddresses.GetPreferredLocalHosts()
        };


    private int GetDirectEndpointPriorityForThisDevice(EnrollmentEndpoint endpoint) =>
        _networkAddresses.GetRemoteEndpointPriority(endpoint.Host);


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
        IsLocalDeviceIdentity(endpoint.DeviceId, endpoint.SignPublicKey, endpoint.TlsCertFingerprint);


    private void RejectLocalDeviceEnrollmentCode(DeviceEnrollmentParsedCode parsed)
    {
        lock (_lock)
        {
            if (_currentSession is not null &&
                string.Equals(_currentSession.SessionId, parsed.SessionId, StringComparison.Ordinal) &&
                DeviceEnrollmentCode.FixedTimeEquals(_currentSession.Secret, parsed.Secret))
                throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.InvalidCode, "The enrollment code belongs to this local device.");
        }

        if (parsed.DirectEndpoints.Any(endpoint =>
                IsLocalDeviceIdentity(endpoint.DeviceId, endpoint.SignPublicKey, endpoint.TlsCertFingerprint)))
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.InvalidCode, "The enrollment code belongs to this local device.");
    }


    private bool IsLocalDeviceIdentity(Guid deviceId, byte[] signPublicKey, string tlsCertFingerprint)
    {
        if (deviceId != Guid.Empty && deviceId == _identity.LocalDeviceId)
            return true;

        if (signPublicKey.Length > 0 && _identity.SignPublicKey.SequenceEqual(signPublicKey))
            return true;

        return FingerprintMatchesLocalDevice(tlsCertFingerprint);
    }


    private bool FingerprintMatchesLocalDevice(string tlsCertFingerprint)
    {
        var remoteFingerprint = FingerprintUtil.Normalize(tlsCertFingerprint);
        var localFingerprint = FingerprintUtil.Normalize(_identity.FingerprintHex);

        if (remoteFingerprint.Length == 0 || localFingerprint.Length == 0)
            return false;

        return localFingerprint.StartsWith(remoteFingerprint, StringComparison.OrdinalIgnoreCase) ||
               remoteFingerprint.StartsWith(localFingerprint, StringComparison.OrdinalIgnoreCase);
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
                    DeactivateEnrollmentDiscoveryLocked();
                    _currentSession.ClearSensitiveData();
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


    private void DeactivateEnrollmentDiscoveryLocked()
    {
        if (_currentSession is not null)
            _localDiscovery.DeactivateEnrollmentSession(_currentSession.SessionId);
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
        DeactivateEnrollmentDiscoveryLocked();
        _currentSession.ClearSensitiveData();
    }


    private async Task RegisterRemoteDeviceAsync(IServiceProvider services, Guid userId, EnrollmentEndpoint endpoint, CancellationToken ct)
    {
        var devices = services.GetRequiredService<IDeviceRepository>();
        var userDevices = services.GetRequiredService<IUserDeviceRepository>();
        var localUserDevices = services.GetRequiredService<ILocalUserDeviceRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var now = DateTimeOffset.UtcNow;

        var device = await FindExistingDeviceForEndpointAsync(devices, endpoint, ct);
        var link = await userDevices.GetAsync(userId, endpoint.DeviceId, ct);

        if (link is not null)
        {
            link.VerifyIntegrity();
            if (!link.IsDeleted)
                throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.NewDeviceRejected, "This profile is already linked to the selected device.");
        }

        if (device is null)
        {
            device = new Device
            {
                Id = endpoint.DeviceId,
                PublicKey = endpoint.AgreementPublicKey,
                SignPublicKey = endpoint.SignPublicKey,
                TlsCertFingerprint = FingerprintUtil.Normalize(endpoint.TlsCertFingerprint),
                DeviceType = endpoint.DeviceType,
                LastSync = now.UtcDateTime,
                LastSeen = now.UtcDateTime,
                IsTrusted = true,
                IsBlocked = false,
                LastModifiedAt = now
            };
            device.GenerateIntegrityHash();
            await devices.AddAsync(device, ct);
        }
        else
        {
            device.IsTrusted = true;
            device.IsBlocked = false;
            device.BlockedReason = null;
            device.BlockedAt = null;
            device.LastSeen = now.UtcDateTime;
            device.LastModifiedAt = now;
            device.TlsCertFingerprint = FingerprintUtil.Normalize(device.TlsCertFingerprint);
            device.GenerateIntegrityHash();
            devices.Update(device);
        }

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
            await userDevices.AddAsync(link, ct);
        }
        else
        {
            link.Device = device;
            link.IsDeleted = false;
            link.DeletedAt = null;
            link.IsSyncOn = true;
            link.LastModifiedAt = now;
            userDevices.Update(link);
        }

        link.GenerateIntegrityHash();
        await EnsureLocalUserDeviceAsync(devices, localUserDevices, userId, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Device?> FindExistingDeviceForEndpointAsync(IDeviceRepository devices, EnrollmentEndpoint endpoint, CancellationToken ct)
    {
        var matches = new List<Device>();

        var byId = await devices.GetByIdWithUserDevicesAsync(endpoint.DeviceId, ct);
        if (byId is not null)
            matches.Add(byId);

        var byFingerprint = await devices.GetByTlsCertFingerprintWithUserDevicesAsync(endpoint.TlsCertFingerprint, ct);
        if (byFingerprint is not null)
            matches.Add(byFingerprint);

        var bySignPublicKey = await devices.GetBySignPublicKeyAsync(endpoint.SignPublicKey, ct);
        if (bySignPublicKey is not null)
            matches.Add(bySignPublicKey);

        var distinctMatches = matches
            .GroupBy(device => device.Id)
            .Select(group => group.First())
            .ToList();

        if (distinctMatches.Count == 0)
            return null;

        if (distinctMatches.Count > 1 || distinctMatches[0].Id != endpoint.DeviceId)
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.DeviceIdentityConflict, "A device with the same cryptographic identity already exists under a different device id.");

        var device = distinctMatches[0];
        foreach (var existingLink in device.UserDevices)
            existingLink.VerifyIntegrity();

        if (!device.SignPublicKey.SequenceEqual(endpoint.SignPublicKey) ||
            !device.PublicKey.SequenceEqual(endpoint.AgreementPublicKey) ||
            !string.Equals(FingerprintUtil.Normalize(device.TlsCertFingerprint), FingerprintUtil.Normalize(endpoint.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase) ||
            device.DeviceType != endpoint.DeviceType)
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.DeviceIdentityConflict, "A different device already uses this device identity.");

        return device;
    }

    private async Task RejectIfPrimaryUserAlreadyLinkedToLocalDeviceAsync(IServiceProvider services, Guid userId, CancellationToken ct)
    {
        var localUserDevices = services.GetRequiredService<ILocalUserDeviceRepository>();
        var localLink = await localUserDevices.GetAsync(userId, ct);
        if (localLink is null)
            return;

        localLink.VerifyIntegrity();
        if (localLink.LocalDeviceIdentityId == _identity.LocalDeviceId)
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.NewDeviceRejected, "This profile is already linked to the local device.");
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

        if (IPAddress.TryParse(sourceHost, out _) &&
            _networkAddresses.GetRemoteEndpointPriority(sourceHost) == int.MinValue)
            return;

        var devices = services.GetRequiredService<IDeviceRepository>();
        var syncIdentities = services.GetRequiredService<ISyncDeviceIdentityService>();
        var syncTasks = services.GetRequiredService<IDeviceSyncTaskService>();

        var device = await devices.GetByIdAsNoTrackingAsync(parsedDeviceId, ct);
        if (device is null || !device.IsTrusted || device.IsBlocked)
            return;

        if (!string.Equals(FingerprintUtil.Normalize(device.TlsCertFingerprint), FingerprintUtil.Normalize(sourceTlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
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
        var users = services.GetRequiredService<IUserRepository>();
        var userDevicesRepository = services.GetRequiredService<IUserDeviceRepository>();
        var groupsRepository = services.GetRequiredService<IGroupRepository>();
        var devicesRepository = services.GetRequiredService<IDeviceRepository>();

        var user = await users.GetByIdAsNoTrackingWithRelationsAsync(userId, ct);
        if (user is null)
            throw new UserNotFoundException();

        var allUserDevices = await userDevicesRepository.ListByUserAsync(userId, ct);
        foreach (var userDevice in allUserDevices)
            userDevice.VerifyIntegrity();

        var userDevices = allUserDevices.Where(ud => !ud.IsDeleted).ToList();
        var userDeviceIds = userDevices.Select(ud => ud.DeviceId).Distinct().ToList();
        var groups = await groupsRepository.ListByUserWithUserIdsAsNoTrackingAsync(userId, ct);
        var devices = await devicesRepository.ListByIdsWithUserDevicesAsNoTrackingAsync(userDeviceIds, _identity.LocalDeviceId, ct);

        foreach (var device in devices)
        {
            foreach (var link in device.UserDevices)
                link.VerifyIntegrity();
            device.GenerateIntegrityHash();
        }
        foreach (var group in groups)
        {
            var integritySource = new Group
            {
                Id = group.Id,
                EncryptedPayload = group.EncryptedPayload,
                LastModifiedAt = group.LastModifiedAt
            };
            integritySource.GenerateIntegrityHash();
            group.IntegrityHash = integritySource.IntegrityHash;
        }
        user.GenerateIntegrityHash();

        var deviceSnapshots = devices.Select(d => new DeviceEnrollmentDeviceSnapshot
        {
            Id = d.Id,
            PublicKey = d.PublicKey,
            SignPublicKey = d.SignPublicKey,
            TlsCertFingerprint = d.TlsCertFingerprint,
            DeviceType = d.DeviceType,
            LastKnownHash = d.LastKnownHash,
            LastSync = UtcDateTimeUtil.ToUtc(d.LastSync),
            LastSeen = UtcDateTimeUtil.ToUtc(d.LastSeen),
            IsTrusted = d.IsTrusted,
            IsBlocked = d.IsBlocked,
            BlockedReason = d.BlockedReason,
            BlockedAt = UtcDateTimeUtil.ToUtc(d.BlockedAt),
            InvalidSyncAttemptCount = d.InvalidSyncAttemptCount,
            LastInvalidSyncAttemptAt = UtcDateTimeUtil.ToUtc(d.LastInvalidSyncAttemptAt),
            LastModifiedAt = UtcDateTimeUtil.ToUtc(d.LastModifiedAt),
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
            LastSync = UtcDateTimeUtil.ToUtc(localDevice.LastSync),
            LastSeen = UtcDateTimeUtil.ToUtc(localDevice.LastSeen),
            IsTrusted = true,
            IsBlocked = false,
            LastModifiedAt = UtcDateTimeUtil.ToUtc(localDevice.LastModifiedAt),
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
                    EncryptedGeneralUserDataPayload = user.EncryptedGeneralUserDataPayload,
                    EncryptedUserPasswordsDataPayload = user.EncryptedUserPasswordsDataPayload,
                    EncryptedUserDevicesDataPayload = user.EncryptedUserDevicesDataPayload,
                    LastModifiedAt = UtcDateTimeUtil.ToUtc(user.LastModifiedAt),
                    UserDataLastModifiedAt = UtcDateTimeUtil.ToUtc(user.UserDataLastModifiedAt),
                    GeneralUserDataLastModifiedAt = UtcDateTimeUtil.ToUtc(user.GeneralUserDataLastModifiedAt),
                    UserPasswordsDataLastModifiedAt = UtcDateTimeUtil.ToUtc(user.UserPasswordsDataLastModifiedAt),
                    UserDevicesDataLastModifiedAt = UtcDateTimeUtil.ToUtc(user.UserDevicesDataLastModifiedAt),
                    IntegrityHash = user.IntegrityHash,
                    GroupIds = user.Groups.Select(g => g.Id).Distinct().ToList()
                }
            ],
            Groups = groups.Select(g => new DeviceEnrollmentGroupSnapshot
            {
                Id = g.Id,
                EncryptedPayload = g.EncryptedPayload,
                LastModifiedAt = UtcDateTimeUtil.ToUtc(g.LastModifiedAt),
                IntegrityHash = g.IntegrityHash,
                UserIds = g.UserIds
            }).ToList(),
            Devices = deviceSnapshots,
            UserDevices = userDevices.Select(ud => new DeviceEnrollmentUserDeviceSnapshot
            {
                UserId = ud.UserId,
                DeviceId = ud.DeviceId,
                IsSyncOn = ud.IsSyncOn,
                IsDeleted = ud.IsDeleted,
                DeletedAt = UtcDateTimeUtil.ToUtc(ud.DeletedAt),
                LastModifiedAt = UtcDateTimeUtil.ToUtc(ud.LastModifiedAt),
                IntegrityHash = ud.IntegrityHash.ToArray()
            }).Append(new DeviceEnrollmentUserDeviceSnapshot
            {
                UserId = localUserDeviceSnapshotSource.UserId,
                DeviceId = localUserDeviceSnapshotSource.DeviceId,
                IsSyncOn = localUserDeviceSnapshotSource.IsSyncOn,
                IsDeleted = localUserDeviceSnapshotSource.IsDeleted,
                DeletedAt = UtcDateTimeUtil.ToUtc(localUserDeviceSnapshotSource.DeletedAt),
                LastModifiedAt = UtcDateTimeUtil.ToUtc(localUserDeviceSnapshotSource.LastModifiedAt),
                IntegrityHash = localUserDeviceSnapshotSource.IntegrityHash.ToArray()
            }).ToList()
        };
    }

    private async Task EnsureEncryptedDeviceDataAsync(IUserService users, User user, Guid token, Guid deviceId, CancellationToken ct)
    {
        using var bundle = await users.GetAndVerifyUserDataBundleAsync(user, token, ct);
        if (bundle.UserDevicesData.Devices.Any(device => device.Id == deviceId))
            return;

        var baseName = DeviceNameUtil.BuildDefaultDeviceName(deviceId);
        var name = BuildUniqueEncryptedDeviceName(bundle.UserDevicesData, baseName, deviceId);
        var deviceData = new UserDeviceData
        {
            Id = deviceId,
            Name = name,
            LinkedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
        deviceData.GenerateIntegrityHash();
        bundle.UserDevicesData.DeletedDevices.RemoveAll(deleted => deleted.Id == deviceData.Id);
        bundle.UserDevicesData.Devices.Add(deviceData);
        await users.UpdateUserDataBundleAsync(bundle, token, UserDataBlobKind.Devices, false, ct);
    }

    private string BuildUniqueEncryptedDeviceName(UserDevicesData userDevicesData, string requestedName, Guid deviceId)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName)
            ? DeviceNameUtil.BuildDefaultDeviceName(deviceId)
            : requestedName.Trim();

        bool IsTaken(string value) => userDevicesData.Devices.Any(device =>
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

        var expectedFingerprint = FingerprintUtil.Normalize(endpoint.TlsCertFingerprint);
        var actualFingerprint = FingerprintUtil.Normalize(info.TlsCertFingerprint);
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
                CodeProof = ByteString.CopyFrom(DeviceEnrollmentCode.BuildEnrollmentInfoProof(parsed.SessionId, parsed.Secret)),
                SourceDatabaseVersion = DatabaseConstants.CurrentDbVersion
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException or InvalidOperationException or CryptographicException or ArgumentException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
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
            UtcDateTimeUtil.NormalizeObjectGraph(snapshot);
            snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, DataCodec.JsonOpts);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException or InvalidOperationException or CryptographicException or ArgumentException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
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
                SourceDatabaseVersion = offset == 0 ? DatabaseConstants.CurrentDbVersion : 0,
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
                SourceDatabaseVersion = DatabaseConstants.CurrentDbVersion,
                SnapshotChunk = ByteString.Empty
            };
        }
    }


    private (bool Ok, DeviceEnrollmentErrorCode ErrorCode, string? Error) ParseEnrollmentReply(CompleteDeviceEnrollmentReply reply)
    {
        if (reply.Ok)
            return (true, DeviceEnrollmentErrorCode.Unknown, null);

        var errorCode = Enum.TryParse<DeviceEnrollmentErrorCode>(reply.ErrorCode, out var parsedErrorCode)
            ? parsedErrorCode
            : DeviceEnrollmentErrorCode.NewDeviceRejected;

        return (false, errorCode, string.IsNullOrWhiteSpace(reply.Error) ? "The new device rejected the enrollment request." : reply.Error);
    }


    private (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptEnrollmentSnapshot(
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


    private byte[] DecryptEnrollmentSnapshot(
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


    private void RejectSensitiveLocalOnlySnapshotPayload(byte[] payload)
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


    private readonly string[] SensitiveLocalOnlySnapshotPropertyNames =
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


    private bool ContainsProperty(JsonElement element, string propertyName)
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
        var devices = services.GetRequiredService<IDeviceRepository>();
        var users = services.GetRequiredService<IUserRepository>();
        var groups = services.GetRequiredService<IGroupRepository>();
        var userDevices = services.GetRequiredService<IUserDeviceRepository>();
        var localUserDevices = services.GetRequiredService<ILocalUserDeviceRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var syncIdentities = services.GetRequiredService<ISyncDeviceIdentityService>();
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        await RemoveLocalDeviceRowsAsync(devices, ct);

        foreach (var deviceSnapshot in snapshot.Devices)
        {
            if (!DeviceTypeDetector.IsValid(deviceSnapshot.DeviceType))
                throw new InvalidDataException("The enrollment snapshot contains an invalid device type.");

            var isLocalDevice = deviceSnapshot.Id == _identity.LocalDeviceId ||
                deviceSnapshot.SignPublicKey.SequenceEqual(_identity.SignPublicKey) ||
                string.Equals(FingerprintUtil.Normalize(deviceSnapshot.TlsCertFingerprint), FingerprintUtil.Normalize(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase);

            if (isLocalDevice)
                continue;

            var device = await FindExistingDeviceForSnapshotAsync(devices, deviceSnapshot, ct);
            if (device is null)
            {
                device = new Device { Id = deviceSnapshot.Id };
                await devices.AddAsync(device, ct);
            }

            device.PublicKey = deviceSnapshot.PublicKey;
            device.SignPublicKey = deviceSnapshot.SignPublicKey;
            device.TlsCertFingerprint = FingerprintUtil.Normalize(deviceSnapshot.TlsCertFingerprint);
            device.DeviceType = deviceSnapshot.DeviceType;
            device.LastKnownHash = deviceSnapshot.LastKnownHash;
            device.LastSync = UtcDateTimeUtil.ToUtc(deviceSnapshot.LastSync);
            device.LastSeen = UtcDateTimeUtil.ToUtc(deviceSnapshot.LastSeen);
            device.IsTrusted = true;
            device.IsBlocked = deviceSnapshot.IsBlocked;
            device.BlockedReason = deviceSnapshot.BlockedReason;
            device.BlockedAt = UtcDateTimeUtil.ToUtc(deviceSnapshot.BlockedAt);
            device.InvalidSyncAttemptCount = deviceSnapshot.InvalidSyncAttemptCount;
            device.LastInvalidSyncAttemptAt = UtcDateTimeUtil.ToUtc(deviceSnapshot.LastInvalidSyncAttemptAt);
            device.LastModifiedAt = UtcDateTimeUtil.ToUtc(deviceSnapshot.LastModifiedAt == default ? now : deviceSnapshot.LastModifiedAt);
            device.GenerateIntegrityHash();
        }

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var userSnapshot in snapshot.Users)
        {
            var user = await users.GetByIdAsync(userSnapshot.UId, ct);
            if (user is null)
            {
                user = new User { UId = userSnapshot.UId };
                await users.AddAsync(user, ct);
            }

            if (userSnapshot.EncryptedPayload.Length == 0 ||
                userSnapshot.EncryptedGeneralUserDataPayload.Length == 0 ||
                userSnapshot.EncryptedUserPasswordsDataPayload.Length == 0 ||
                userSnapshot.EncryptedUserDevicesDataPayload.Length == 0 ||
                userSnapshot.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                throw new InvalidDataException("The enrollment snapshot contains incomplete encrypted user data.");

            user.UsernameHash = userSnapshot.UsernameHash;
            user.UsernameSalt = userSnapshot.UsernameSalt;
            user.PasswordSalt = userSnapshot.PasswordSalt;
            user.EncryptedPayload = userSnapshot.EncryptedPayload;
            user.EncryptedGeneralUserDataPayload = userSnapshot.EncryptedGeneralUserDataPayload;
            user.EncryptedUserPasswordsDataPayload = userSnapshot.EncryptedUserPasswordsDataPayload;
            user.EncryptedUserDevicesDataPayload = userSnapshot.EncryptedUserDevicesDataPayload;
            user.SavedKey = null;
            user.LastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.LastModifiedAt == default ? now : userSnapshot.LastModifiedAt);
            user.UserDataLastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.UserDataLastModifiedAt == default ? user.LastModifiedAt : userSnapshot.UserDataLastModifiedAt);
            user.GeneralUserDataLastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.GeneralUserDataLastModifiedAt == default ? user.LastModifiedAt : userSnapshot.GeneralUserDataLastModifiedAt);
            user.UserPasswordsDataLastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.UserPasswordsDataLastModifiedAt == default ? user.LastModifiedAt : userSnapshot.UserPasswordsDataLastModifiedAt);
            user.UserDevicesDataLastModifiedAt = UtcDateTimeUtil.ToUtc(userSnapshot.UserDevicesDataLastModifiedAt == default ? user.LastModifiedAt : userSnapshot.UserDevicesDataLastModifiedAt);
            user.GenerateIntegrityHash();
            if (!Hashing.Verify(userSnapshot.IntegrityHash, user.IntegrityHash))
                throw new InvalidDataException("The enrollment snapshot contains invalid user integrity data.");
        }

        foreach (var groupSnapshot in snapshot.Groups)
        {
            var group = await groups.GetByIdWithUsersAsync(groupSnapshot.Id, ct);
            if (group is null)
            {
                group = new Group { Id = groupSnapshot.Id };
                await groups.AddAsync(group, ct);
            }

            group.EncryptedPayload = groupSnapshot.EncryptedPayload;
            group.LastModifiedAt = UtcDateTimeUtil.ToUtc(groupSnapshot.LastModifiedAt == default ? now : groupSnapshot.LastModifiedAt);
            group.IntegrityHash = groupSnapshot.IntegrityHash;
        }

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var groupSnapshot in snapshot.Groups)
        {
            var group = await groups.GetByIdWithUsersAsync(groupSnapshot.Id, ct)
                ?? throw new InvalidDataException("The enrollment snapshot group could not be persisted.");
            var userIds = groupSnapshot.UserIds.Where(id => id != Guid.Empty).Distinct().ToHashSet();

            foreach (var user in group.Users.Where(u => !userIds.Contains(u.UId)).ToList())
                group.Users.Remove(user);

            foreach (var userId in userIds)
            {
                if (group.Users.Any(u => u.UId == userId))
                    continue;

                var user = await users.GetByIdAsync(userId, ct);
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
            if (linkSnapshot.LastModifiedAt == default || linkSnapshot.IntegrityHash.Length != Hashing.SHA256HashSizeInBytes)
                throw new InvalidDataException("The enrollment snapshot contains an incomplete user-device relationship.");

            var verifiedSnapshotLink = new UserDevice
            {
                UserId = linkSnapshot.UserId,
                DeviceId = linkSnapshot.DeviceId,
                IsSyncOn = linkSnapshot.IsSyncOn,
                IsDeleted = linkSnapshot.IsDeleted,
                DeletedAt = UtcDateTimeUtil.ToUtc(linkSnapshot.DeletedAt),
                LastModifiedAt = UtcDateTimeUtil.ToUtc(linkSnapshot.LastModifiedAt)
            };
            verifiedSnapshotLink.GenerateIntegrityHash();
            if (!Hashing.Verify(linkSnapshot.IntegrityHash, verifiedSnapshotLink.IntegrityHash))
                throw new InvalidDataException("The enrollment snapshot contains an invalid user-device relationship hash.");

            if (linkSnapshot.DeviceId == _identity.LocalDeviceId)
                continue;

            var remoteDevice = await devices.GetByIdAsync(linkSnapshot.DeviceId, ct);
            if (remoteDevice is null)
                continue;

            var link = await GetOrCreateUserDeviceAsync(userDevices, linkSnapshot.UserId, linkSnapshot.DeviceId, ct);
            link.Device = remoteDevice;
            link.IsSyncOn = linkSnapshot.IsSyncOn;
            link.IsDeleted = linkSnapshot.IsDeleted;
            link.DeletedAt = UtcDateTimeUtil.ToUtc(linkSnapshot.DeletedAt);
            link.LastModifiedAt = UtcDateTimeUtil.ToUtc(linkSnapshot.LastModifiedAt);
            link.IntegrityHash = verifiedSnapshotLink.IntegrityHash.ToArray();
        }

        await EnsureLocalUserDeviceAsync(devices, localUserDevices, snapshot.PrimaryUserId, ct);

        await unitOfWork.SaveChangesAsync(ct);
        await RemoveLocalDeviceRowsAsync(devices, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var trustedDevices = await devices.ListTrustedUnblockedAsync(ct);
        foreach (var device in trustedDevices)
            syncIdentities.TryAdd(device);
    }


    private async Task<Device?> FindExistingDeviceForSnapshotAsync(IDeviceRepository devices, DeviceEnrollmentDeviceSnapshot snapshot, CancellationToken ct)
    {
        var matches = new List<Device>();

        var byId = await devices.GetByIdAsync(snapshot.Id, ct);
        if (byId is not null)
            matches.Add(byId);

        var byFingerprint = await devices.GetByTlsCertFingerprintAsync(snapshot.TlsCertFingerprint, ct);
        if (byFingerprint is not null)
            matches.Add(byFingerprint);

        var bySignPublicKey = await devices.GetBySignPublicKeyAsync(snapshot.SignPublicKey, ct);
        if (bySignPublicKey is not null)
            matches.Add(bySignPublicKey);

        var distinctMatches = matches
            .GroupBy(device => device.Id)
            .Select(group => group.First())
            .ToList();

        if (distinctMatches.Count == 0)
            return null;

        if (distinctMatches.Count > 1 || distinctMatches[0].Id != snapshot.Id)
            throw new InvalidDataException("The enrollment snapshot contains conflicting duplicate device identity data.");

        var device = distinctMatches[0];
        if (!device.SignPublicKey.SequenceEqual(snapshot.SignPublicKey) ||
            !device.PublicKey.SequenceEqual(snapshot.PublicKey) ||
            !string.Equals(FingerprintUtil.Normalize(device.TlsCertFingerprint), FingerprintUtil.Normalize(snapshot.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase) ||
            device.DeviceType != snapshot.DeviceType)
            throw new InvalidDataException("The enrollment snapshot contains conflicting duplicate device identity data.");

        return device;
    }


    private async Task<UserDevice> GetOrCreateUserDeviceAsync(
        IUserDeviceRepository userDevices,
        Guid userId,
        Guid deviceId,
        CancellationToken ct)
    {
        var existing = await userDevices.GetAsync(userId, deviceId, ct);
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

        await userDevices.AddAsync(created, ct);
        return created;
    }


    private async Task RemoveLocalDeviceRowsAsync(IDeviceRepository devices, CancellationToken ct)
    {
        var localRows = await devices.ListLocalSelfDevicesAsync(
            _identity.LocalDeviceId,
            _identity.SignPublicKey,
            _identity.FingerprintHex,
            ct);

        foreach (var localRow in localRows)
            devices.Delete(localRow);
    }


    private async Task EnsureLocalUserDeviceAsync(
        IDeviceRepository devices,
        ILocalUserDeviceRepository localUserDevices,
        Guid userId,
        CancellationToken ct)
    {
        await RemoveLocalDeviceRowsAsync(devices, ct);
        var link = await localUserDevices.GetAsync(userId, ct);
        if (link is null)
        {
            var localUserDevice = new LocalUserDevice
            {
                UserId = userId,
                LocalDeviceIdentityId = _identity.LocalDeviceId,
                IsSyncOn = true
            };
            localUserDevice.GenerateIntegrityHash();
            await localUserDevices.AddAsync(localUserDevice, ct);
            return;
        }

        link.VerifyIntegrity();
        link.LocalDeviceIdentityId = _identity.LocalDeviceId;
        link.GenerateIntegrityHash();
        localUserDevices.Update(link);
    }


    private async Task QueueInitialSyncAsync(IServiceProvider services, Guid userId, Guid newDeviceId, CancellationToken ct)
    {
        var groups = services.GetRequiredService<IGroupRepository>();
        var syncQueue = services.GetRequiredService<ISyncQueueService>();
        var groupIds = await groups.ListIdsByUserAsync(userId, ct);

        await syncQueue.EnqueueAsync(new SyncItem { ModelId = userId, ModelType = SyncModelType.User, ChangeType = SyncChangeType.Updated }, ct);

        foreach (var groupId in groupIds)
            await syncQueue.EnqueueAsync(new SyncItem { ModelId = groupId, ModelType = SyncModelType.Group, ChangeType = SyncChangeType.Updated }, ct);

        await syncQueue.EnqueueAsync(new SyncItem { ModelId = newDeviceId, ModelType = SyncModelType.Device, ChangeType = SyncChangeType.Created }, ct);
        await syncQueue.EnqueueAsync(new SyncItem { ModelId = SyncIdentityUtil.BuildUserDeviceModelId(userId, newDeviceId), ModelType = SyncModelType.UserDevice, ChangeType = SyncChangeType.Created }, ct);
    }




    public void Dispose()
    {
        lock (_lock)
        {
            CancelEnrollmentExpirationLocked();
            DeactivateEnrollmentDiscoveryLocked();
            _currentSession?.ClearSensitiveData();
            _currentSession = null;
        }
    }
}
