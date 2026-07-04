using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.State;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Enrollment;
using System.Security.Cryptography;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Owns enrollment session state and orchestrates the high-level enrollment protocol. Endpoint
/// validation, device registration, snapshot handling, transfer, and import are delegated to focused services.
/// </summary>
public sealed class DeviceEnrollmentService : IDeviceEnrollmentService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _identity;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly ISyncRuntimeService _syncRuntime;
    private readonly ILocalDiscoveryService _localDiscovery;
    private readonly IDeviceEnrollmentEndpointService _endpointService;
    private readonly IDeviceEnrollmentRegistrationService _registrationService;
    private readonly IDeviceEnrollmentSnapshotService _snapshotService;
    private readonly IDeviceEnrollmentSnapshotTransferService _snapshotTransferService;
    private readonly IDeviceEnrollmentSnapshotImporterService _snapshotImporter;
    private readonly object _lock = new();
    private EnrollmentSession? _currentSession;
    private CancellationTokenSource? _enrollmentExpirationCancellation;

    public DeviceEnrollmentService(
        IServiceScopeFactory scopeFactory,
        IDeviceIdentityService identity,
        IDiscoveredDeviceEndpointCache endpointCache,
        ISyncRuntimeService syncRuntime,
        ILocalDiscoveryService localDiscovery,
        IDeviceEnrollmentEndpointService endpointService,
        IDeviceEnrollmentRegistrationService registrationService,
        IDeviceEnrollmentSnapshotService snapshotService,
        IDeviceEnrollmentSnapshotTransferService snapshotTransferService,
        IDeviceEnrollmentSnapshotImporterService snapshotImporter)
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
        _endpointCache = endpointCache;
        _syncRuntime = syncRuntime;
        _localDiscovery = localDiscovery;
        _endpointService = endpointService;
        _registrationService = registrationService;
        _snapshotService = snapshotService;
        _snapshotTransferService = snapshotTransferService;
        _snapshotImporter = snapshotImporter;
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
                directEndpointInfo = _endpointService.BuildDirectEndpointInfo();
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

            await _endpointService.VerifyLocalEnrollmentListenerAsync(session.SessionId, session.Secret, directEndpointInfo, ct);
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
            .Select(_endpointService.ToEnrollmentEndpoint)
            .Where(endpoint => !_endpointService.IsLocalEndpoint(endpoint))
            .GroupBy(endpoint => $"{endpoint.Host}:{endpoint.Port}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(endpoint => new { Endpoint = endpoint, Priority = _endpointService.GetDirectEndpointPriorityForThisDevice(endpoint) })
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
                _endpointService.IsLocalDeviceIdentity(endpoint.DeviceId, endpoint.SignPublicKey, endpoint.TlsCertFingerprint)))
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.InvalidCode, "The enrollment code belongs to this local device.");
    }


    private async Task CompleteEnrollmentWithEndpointAsync(Guid token, DeviceEnrollmentParsedCode parsed, EnrollmentEndpoint endpoint, CancellationToken ct)
    {
        DeviceEnrollmentTrace.Info($"Enrollment connection and identity check started for {endpoint.Host}:{endpoint.Port}. HasEmbeddedIdentity={endpoint.DeviceId != Guid.Empty}.");
        endpoint = await _endpointService.ResolveEndpointIdentityAsync(endpoint, parsed, ct);
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
            await _registrationService.RegisterRemoteDeviceAsync(scope.ServiceProvider, user.UId, endpoint, ct);
            await _snapshotService.EnsureEncryptedDeviceDataAsync(users, user, token, endpoint.DeviceId, ct);
            var snapshot = await _snapshotService.BuildAsync(scope.ServiceProvider, user.UId, ct);

            var proof = DeviceEnrollmentCode.BuildCompletionProof(
                parsed.SessionId,
                parsed.Secret,
                _identity.LocalDeviceId.ToString("N"),
                _identity.SignPublicKey,
                _identity.FingerprintHex);

            var result = await _snapshotTransferService.SendAsync(endpoint, parsed.SessionId, parsed.Secret, proof, snapshot, ct);
            if (!result.Ok)
                throw new DeviceEnrollmentException(result.ErrorCode, result.Error ?? "The new device rejected the enrollment request.");

            await _registrationService.QueueInitialSyncAsync(scope.ServiceProvider, user.UId, endpoint.DeviceId, ct);
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

        DeviceEnrollmentSnapshot snapshot;
        try
        {
            snapshot = _snapshotService.DecryptAndDeserialize(
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

        try
        {
            using var scope = _scopeFactory.CreateScope();
            DeviceEnrollmentTrace.Info($"Importing incoming enrollment snapshot. Users={snapshot.Users.Count}, Groups={snapshot.Groups.Count}, Devices={snapshot.Devices.Count}, UserDevices={snapshot.UserDevices.Count}.");
            await _registrationService.RejectIfPrimaryUserAlreadyLinkedToLocalDeviceAsync(scope.ServiceProvider, snapshot.PrimaryUserId, ct);
            await _snapshotImporter.ImportAsync(scope.ServiceProvider, snapshot, ct);
            await _syncRuntime.RefreshSyncEnabledAsync(ct);
            await _registrationService.CacheIncomingEnrollmentSourceEndpointAsync(scope.ServiceProvider, sourceDeviceId, sourceTlsCertFingerprint, sourceHost, ct);
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
