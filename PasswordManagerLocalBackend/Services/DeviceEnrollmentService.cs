using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Makaretu.Dns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocalBackend.Abstractions.Persistence;
using PasswordManagerLocalBackend.Abstractions.Repositories;
using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Persistence;
using PasswordManagerLocalBackend.Responses;
using PasswordManagerLocalBackend.Security;
using PasswordManagerLocalBackend.Sync;
using PasswordManagerLocalBackend.Utils;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDeviceIdentityService _identity;
    private readonly object _lock = new();
    private EnrollmentSession? _currentSession;

    public DeviceEnrollmentService(IServiceScopeFactory scopeFactory, IDeviceIdentityService identity)
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
    }




    public Task<DeviceEnrollmentCodeResponse> StartEnrollmentAsync(CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            throw new InvalidOperationException("Device synchronization is disabled on this device.");

        lock (_lock)
        {
            StopAdvertisingLocked();

            var generated = DeviceEnrollmentCode.Create();
            var session = new EnrollmentSession
            {
                SessionId = generated.SessionId,
                Secret = generated.Secret,
                Code = generated.Code,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                State = DeviceEnrollmentState.Waiting
            };

            StartAdvertisingLocked(session);
            _currentSession = session;

            return Task.FromResult(new DeviceEnrollmentCodeResponse
            {
                Code = session.Code,
                ExpiresAt = session.ExpiresAt
            });
        }
    }


    public Task<DeviceEnrollmentStatusResponse> GetEnrollmentStatusAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_currentSession is null)
                return Task.FromResult(new DeviceEnrollmentStatusResponse { State = DeviceEnrollmentState.None });

            ExpireSessionIfNeededLocked();

            return Task.FromResult(new DeviceEnrollmentStatusResponse
            {
                State = _currentSession.State,
                ErrorMessage = _currentSession.ErrorMessage,
                ExpiresAt = _currentSession.ExpiresAt
            });
        }
    }


    public Task CancelEnrollmentAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            StopAdvertisingLocked();
            _currentSession = null;
        }

        return Task.CompletedTask;
    }


    public async Task AddDeviceByCodeAsync(Guid token, string code, CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            throw new InvalidOperationException("Synchronization is disabled on this device. Turn synchronization on before adding a new device.");

        var parsed = DeviceEnrollmentCode.Parse(code);
        using var scope = _scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserService>();
        var user = await users.GetAndVerifyUserAsync(token, ct);

        var endpoint = await FindEnrollmentEndpointAsync(parsed, ct);
        await RegisterRemoteDeviceAsync(scope.ServiceProvider, user.UId, endpoint, ct);
        var snapshot = await BuildSnapshotAsync(scope.ServiceProvider, user.UId, ct);

        var proof = DeviceEnrollmentCode.BuildCompletionProof(
            parsed.SessionId,
            parsed.Secret,
            _identity.LocalDeviceId.ToString("N"),
            _identity.SignPublicKey,
            _identity.FingerprintHex);

        var ok = await SendEnrollmentSnapshotAsync(endpoint, parsed.SessionId, proof, snapshot, ct);
        if (!ok)
            throw new InvalidOperationException("The new device rejected the enrollment request.");

        await QueueInitialSyncAsync(scope.ServiceProvider, user.UId, endpoint.DeviceId, ct);
    }


    public async Task<(bool Ok, string? Error)> CompleteIncomingEnrollmentAsync(
        string sessionId,
        byte[] codeProof,
        byte[] snapshotBytes,
        string sourceDeviceId,
        byte[] sourceSignPublicKey,
        string sourceTlsCertFingerprint,
        string actualClientTlsCertFingerprint,
        CancellationToken ct = default)
    {
        EnrollmentSession? session;

        lock (_lock)
        {
            ExpireSessionIfNeededLocked();
            session = _currentSession;

            if (session is null || session.State != DeviceEnrollmentState.Waiting)
                return (false, "No active enrollment session was found.");

            if (!string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
                return (false, "The enrollment session does not match.");

            if (DateTimeOffset.UtcNow > session.ExpiresAt)
            {
                session.State = DeviceEnrollmentState.Expired;
                session.ErrorMessage = "The enrollment code expired.";
                StopAdvertisingLocked();
                return (false, session.ErrorMessage);
            }
        }

        if (!string.Equals(NormalizeFingerprint(sourceTlsCertFingerprint), NormalizeFingerprint(actualClientTlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
            return await FailIncomingAsync("The source device TLS certificate does not match the enrollment request.");

        var expectedProof = DeviceEnrollmentCode.BuildCompletionProof(
            sessionId,
            session.Secret,
            sourceDeviceId,
            sourceSignPublicKey,
            sourceTlsCertFingerprint);

        if (!DeviceEnrollmentCode.FixedTimeEquals(expectedProof, codeProof))
            return await FailIncomingAsync("The enrollment code proof is invalid.");

        DeviceEnrollmentSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<DeviceEnrollmentSnapshot>(snapshotBytes);
        }
        catch (JsonException ex)
        {
            return await FailIncomingAsync($"The received profile data is invalid: {ex.Message}");
        }

        if (snapshot is null || snapshot.PrimaryUserId == Guid.Empty)
            return await FailIncomingAsync("The received profile data is empty.");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await ImportSnapshotAsync(scope.ServiceProvider, snapshot, ct);
        }
        catch (Exception ex)
        {
            return await FailIncomingAsync(ex.Message);
        }

        lock (_lock)
        {
            if (_currentSession is not null)
            {
                _currentSession.State = DeviceEnrollmentState.Completed;
                _currentSession.ErrorMessage = null;
                StopAdvertisingLocked();
            }
        }

        return (true, null);

        Task<(bool Ok, string? Error)> FailIncomingAsync(string message)
        {
            lock (_lock)
            {
                if (_currentSession is not null)
                {
                    _currentSession.State = DeviceEnrollmentState.Failed;
                    _currentSession.ErrorMessage = message;
                    StopAdvertisingLocked();
                }
            }

            return Task.FromResult<(bool Ok, string? Error)>((false, message));
        }
    }


    private void StartAdvertisingLocked(EnrollmentSession session)
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

        session.Discovery.Advertise(session.Profile);
        session.Discovery.Announce(session.Profile);
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
        _currentSession.ErrorMessage = "The enrollment code expired.";
        StopAdvertisingLocked();
    }


    private async Task<EnrollmentEndpoint> FindEnrollmentEndpointAsync(DeviceEnrollmentParsedCode parsed, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<EnrollmentEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discovery = new ServiceDiscovery();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        discovery.ServiceInstanceDiscovered += OnDiscovered;
        discovery.QueryServiceInstances(MdnsServiceType);

        using (timeout.Token.Register(() => tcs.TrySetCanceled(timeout.Token)))
        {
            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("The new device could not be found on the local network.");
            }
            finally
            {
                discovery.ServiceInstanceDiscovered -= OnDiscovered;
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

                var host = await ResolveHostAsync(mdns, srv.Target, timeout.Token);
                if (host is null)
                    return;

                tcs.TrySetResult(new EnrollmentEndpoint
                {
                    Host = host,
                    Port = srv.Port,
                    DeviceId = deviceId,
                    TlsCertFingerprint = tlsFp,
                    SignPublicKey = signPub,
                    AgreementPublicKey = agreePub
                });
            }
            catch
            {
            }
        }
    }


    private static async Task<IReadOnlyDictionary<string, string>?> ResolveTxtAsync(MulticastService mdns, DomainName instance, CancellationToken ct)
    {
        var query = new Message();
        query.Questions.Add(new Question { Name = instance, Type = DnsType.TXT });

        var response = await mdns.ResolveAsync(query, ct);
        var record = response.Answers.OfType<TXTRecord>().FirstOrDefault();
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
        return response.Answers.OfType<SRVRecord>().FirstOrDefault();
    }


    private static async Task<string?> ResolveHostAsync(MulticastService mdns, DomainName target, CancellationToken ct)
    {
        var aQuery = new Message();
        aQuery.Questions.Add(new Question { Name = target, Type = DnsType.A });

        var aResponse = await mdns.ResolveAsync(aQuery, ct);
        var a = aResponse.Answers.OfType<ARecord>().FirstOrDefault();
        if (a is not null)
            return a.Address.ToString();

        var aaaaQuery = new Message();
        aaaaQuery.Questions.Add(new Question { Name = target, Type = DnsType.AAAA });

        var aaaaResponse = await mdns.ResolveAsync(aaaaQuery, ct);
        var aaaa = aaaaResponse.Answers.OfType<AAAARecord>().FirstOrDefault();
        return aaaa?.Address.ToString();
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

            if (!txt.TryGetValue("tlsfp", out tlsFp) || string.IsNullOrWhiteSpace(tlsFp))
                return false;

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
        var syncQueue = services.GetRequiredService<ISyncQueueService>();
        var syncIdentities = services.GetRequiredService<ISyncDeviceIdentityService>();
        var now = DateTimeOffset.UtcNow;

        var device = await db.Devices
            .Include(d => d.UserDevices)
            .FirstOrDefaultAsync(d => d.Id == endpoint.DeviceId, ct);

        if (device is null)
        {
            device = new Device
            {
                Id = endpoint.DeviceId,
                PublicKey = endpoint.AgreementPublicKey,
                SignPublicKey = endpoint.SignPublicKey,
                TlsCertFingerprint = endpoint.TlsCertFingerprint,
                DeviceName = DeviceNameUtil.BuildDefaultDeviceName(endpoint.DeviceId),
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
                !string.Equals(NormalizeFingerprint(device.TlsCertFingerprint), NormalizeFingerprint(endpoint.TlsCertFingerprint), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A different device already uses this device identity.");

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
                Name = await BuildUniqueDeviceNameAsync(db, userId, endpoint.DeviceId, device.DeviceName, ct),
                IsSyncEnabled = true,
                IsDeleted = false,
                LinkedAt = now,
                LastModifiedAt = now
            };
            await db.UserDevices.AddAsync(link, ct);
        }
        else
        {
            link.IsDeleted = false;
            link.DeletedAt = null;
            link.IsSyncEnabled = true;
            link.LastModifiedAt = now;
        }

        await EnsureLocalDeviceAndLinkAsync(db, userId, ct);
        await db.SaveChangesAsync(ct);
        syncIdentities.TryAdd(device);

        await syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = device.Id,
            ModelType = SyncModelType.Device,
            ChangeType = SyncChangeType.Created
        }, ct);

        await syncQueue.EnqueueAsync(new SyncItem
        {
            ModelId = SyncIdentityUtil.BuildUserDeviceModelId(link.UserId, link.DeviceId),
            ModelType = SyncModelType.UserDevice,
            ChangeType = SyncChangeType.Created
        }, ct);
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

        var userDeviceIds = await db.UserDevices.AsNoTracking()
            .Where(ud => ud.UserId == userId && !ud.IsDeleted)
            .Select(ud => ud.DeviceId)
            .Distinct()
            .ToListAsync(ct);

        var groups = await db.Groups.AsNoTracking()
            .Include(g => g.Users)
            .Where(g => g.Users.Any(u => u.UId == userId))
            .ToListAsync(ct);

        var devices = await db.Devices.AsNoTracking()
            .Include(d => d.UserDevices)
            .Where(d => userDeviceIds.Contains(d.Id))
            .ToListAsync(ct);

        var userDevices = await db.UserDevices.AsNoTracking()
            .Where(ud => ud.UserId == userId && !ud.IsDeleted)
            .ToListAsync(ct);

        foreach (var device in devices)
            device.GenerateIntegrityHash();

        foreach (var group in groups)
            group.GenerateIntegrityHash();

        user.GenerateIntegrityHash();

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
            Devices = devices.Select(d => new DeviceEnrollmentDeviceSnapshot
            {
                Id = d.Id,
                PublicKey = d.PublicKey,
                SignPublicKey = d.SignPublicKey,
                TlsCertFingerprint = d.TlsCertFingerprint,
                DeviceName = d.DeviceName,
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
            }).ToList(),
            UserDevices = userDevices.Select(ud => new DeviceEnrollmentUserDeviceSnapshot
            {
                UserId = ud.UserId,
                DeviceId = ud.DeviceId,
                Name = ud.Name,
                IsSyncEnabled = ud.IsSyncEnabled,
                IsDeleted = ud.IsDeleted,
                LinkedAt = ud.LinkedAt,
                DeletedAt = ud.DeletedAt,
                LastModifiedAt = ud.LastModifiedAt
            }).ToList()
        };
    }


    private async Task<bool> SendEnrollmentSnapshotAsync(EnrollmentEndpoint endpoint, string sessionId, byte[] proof, DeviceEnrollmentSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            using var handler = CreatePinnedHandler(endpoint.TlsCertFingerprint);
            using var channel = GrpcChannel.ForAddress(BuildAddress(endpoint.Host, endpoint.Port), new GrpcChannelOptions { HttpHandler = handler });
            var client = new SyncGrpc.SyncGrpcClient(channel);
            var snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);

            var reply = await client.CompleteDeviceEnrollmentAsync(new CompleteDeviceEnrollmentRequest
            {
                SessionId = sessionId,
                CodeProof = ByteString.CopyFrom(proof),
                Snapshot = ByteString.CopyFrom(snapshotBytes),
                SourceDeviceId = _identity.LocalDeviceId.ToString("N"),
                SourceSignPub = ByteString.CopyFrom(_identity.SignPublicKey),
                SourceTlsCertFingerprint = _identity.FingerprintHex
            }, cancellationToken: ct);

            return reply.Ok;
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
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }


    private HttpClientHandler CreatePinnedHandler(string serverFingerprintHex)
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


    private async Task ImportSnapshotAsync(IServiceProvider services, DeviceEnrollmentSnapshot snapshot, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var syncIdentities = services.GetRequiredService<ISyncDeviceIdentityService>();
        var now = DateTimeOffset.UtcNow;

        await EnsureLocalDeviceOnlyAsync(db, ct);

        foreach (var deviceSnapshot in snapshot.Devices)
        {
            var isLocalDevice = deviceSnapshot.Id == _identity.LocalDeviceId ||
                deviceSnapshot.SignPublicKey.SequenceEqual(_identity.SignPublicKey) ||
                string.Equals(NormalizeFingerprint(deviceSnapshot.TlsCertFingerprint), NormalizeFingerprint(_identity.FingerprintHex), StringComparison.OrdinalIgnoreCase);

            var device = await db.Devices.Include(d => d.UserDevices).FirstOrDefaultAsync(d => d.Id == deviceSnapshot.Id, ct);
            if (device is null)
            {
                device = new Device { Id = deviceSnapshot.Id };
                await db.Devices.AddAsync(device, ct);
            }

            if (isLocalDevice)
            {
                device.PublicKey = _identity.AgreementPublicKey;
                device.SignPublicKey = _identity.SignPublicKey;
                device.TlsCertFingerprint = _identity.FingerprintHex;
                device.DeviceName = _identity.DeviceName;
                device.LastSync = now.UtcDateTime;
                device.LastSeen = now.UtcDateTime;
                device.IsTrusted = true;
                device.IsBlocked = false;
                device.BlockedReason = null;
                device.BlockedAt = null;
            }
            else
            {
                device.PublicKey = deviceSnapshot.PublicKey;
                device.SignPublicKey = deviceSnapshot.SignPublicKey;
                device.TlsCertFingerprint = deviceSnapshot.TlsCertFingerprint;
                device.DeviceName = deviceSnapshot.DeviceName;
                device.LastSync = deviceSnapshot.LastSync;
                device.LastSeen = deviceSnapshot.LastSeen;
                device.IsTrusted = true;
                device.IsBlocked = deviceSnapshot.IsBlocked;
                device.BlockedReason = deviceSnapshot.BlockedReason;
                device.BlockedAt = deviceSnapshot.BlockedAt;
            }

            device.LastKnownHash = deviceSnapshot.LastKnownHash;
            device.InvalidSyncAttemptCount = isLocalDevice ? 0 : deviceSnapshot.InvalidSyncAttemptCount;
            device.LastInvalidSyncAttemptAt = isLocalDevice ? null : deviceSnapshot.LastInvalidSyncAttemptAt;
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

        foreach (var linkSnapshot in snapshot.UserDevices)
        {
            var link = await db.UserDevices.FirstOrDefaultAsync(ud => ud.UserId == linkSnapshot.UserId && ud.DeviceId == linkSnapshot.DeviceId, ct);
            if (link is null)
            {
                link = new UserDevice
                {
                    UserId = linkSnapshot.UserId,
                    DeviceId = linkSnapshot.DeviceId
                };
                await db.UserDevices.AddAsync(link, ct);
            }

            link.Name = string.IsNullOrWhiteSpace(linkSnapshot.Name) ? DeviceNameUtil.BuildDefaultDeviceName(linkSnapshot.DeviceId) : linkSnapshot.Name.Trim();
            link.IsSyncEnabled = linkSnapshot.IsSyncEnabled;
            link.IsDeleted = linkSnapshot.IsDeleted;
            link.LinkedAt = linkSnapshot.LinkedAt == default ? now : linkSnapshot.LinkedAt;
            link.DeletedAt = linkSnapshot.DeletedAt;
            link.LastModifiedAt = linkSnapshot.LastModifiedAt == default ? now : linkSnapshot.LastModifiedAt;
        }

        var localLink = await db.UserDevices.FirstOrDefaultAsync(ud => ud.UserId == snapshot.PrimaryUserId && ud.DeviceId == _identity.LocalDeviceId, ct);
        if (localLink is null)
        {
            localLink = new UserDevice
            {
                UserId = snapshot.PrimaryUserId,
                DeviceId = _identity.LocalDeviceId,
                Name = await BuildUniqueDeviceNameAsync(db, snapshot.PrimaryUserId, _identity.LocalDeviceId, _identity.DeviceName, ct),
                IsSyncEnabled = true,
                IsDeleted = false,
                LinkedAt = now,
                LastModifiedAt = now
            };
            await db.UserDevices.AddAsync(localLink, ct);
        }
        else
        {
            localLink.IsSyncEnabled = true;
            localLink.IsDeleted = false;
            localLink.DeletedAt = null;
            localLink.LastModifiedAt = now;
        }

        await db.SaveChangesAsync(ct);

        var trustedDevices = await db.Devices
            .Where(d => d.Id != _identity.LocalDeviceId && d.IsTrusted && !d.IsBlocked)
            .ToListAsync(ct);

        foreach (var device in trustedDevices)
            syncIdentities.TryAdd(device);
    }


    private async Task EnsureLocalDeviceOnlyAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == _identity.LocalDeviceId, ct);
        if (device is null)
        {
            device = new Device
            {
                Id = _identity.LocalDeviceId,
                PublicKey = _identity.AgreementPublicKey,
                SignPublicKey = _identity.SignPublicKey,
                TlsCertFingerprint = _identity.FingerprintHex,
                DeviceName = _identity.DeviceName,
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
            device.PublicKey = _identity.AgreementPublicKey;
            device.SignPublicKey = _identity.SignPublicKey;
            device.TlsCertFingerprint = _identity.FingerprintHex;
            device.DeviceName = _identity.DeviceName;
            device.IsTrusted = true;
            device.IsBlocked = false;
            device.LastSeen = now.UtcDateTime;
            device.LastModifiedAt = now;
            device.GenerateIntegrityHash();
        }
    }


    private async Task EnsureLocalDeviceAndLinkAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == _identity.LocalDeviceId, ct);
        if (device is null)
        {
            device = new Device
            {
                Id = _identity.LocalDeviceId,
                PublicKey = _identity.AgreementPublicKey,
                SignPublicKey = _identity.SignPublicKey,
                TlsCertFingerprint = _identity.FingerprintHex,
                DeviceName = _identity.DeviceName,
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
            device.PublicKey = _identity.AgreementPublicKey;
            device.SignPublicKey = _identity.SignPublicKey;
            device.TlsCertFingerprint = _identity.FingerprintHex;
            device.DeviceName = _identity.DeviceName;
            device.IsTrusted = true;
            device.IsBlocked = false;
            device.LastSeen = now.UtcDateTime;
            device.LastModifiedAt = now;
            device.GenerateIntegrityHash();
        }

        var link = await db.UserDevices.FirstOrDefaultAsync(ud => ud.UserId == userId && ud.DeviceId == _identity.LocalDeviceId, ct);
        if (link is null)
        {
            await db.UserDevices.AddAsync(new UserDevice
            {
                UserId = userId,
                DeviceId = _identity.LocalDeviceId,
                Name = await BuildUniqueDeviceNameAsync(db, userId, _identity.LocalDeviceId, _identity.DeviceName, ct),
                IsSyncEnabled = true,
                IsDeleted = false,
                LinkedAt = now,
                LastModifiedAt = now
            }, ct);
        }
        else
        {
            link.IsDeleted = false;
            link.DeletedAt = null;
            link.IsSyncEnabled = true;
            link.LastModifiedAt = now;
        }
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
    }


    private static async Task<string> BuildUniqueDeviceNameAsync(AppDbContext db, Guid userId, Guid deviceId, string requestedName, CancellationToken ct)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? DeviceNameUtil.BuildDefaultDeviceName(deviceId) : requestedName.Trim();
        if (!await IsNameTakenAsync(db, userId, baseName, deviceId, ct))
            return baseName;

        for (var i = 2; i < 100; i++)
        {
            var suffix = $"-{i}";
            var prefixLength = Math.Min(baseName.Length, 64 - suffix.Length);
            var name = baseName[..prefixLength] + suffix;

            if (!await IsNameTakenAsync(db, userId, name, deviceId, ct))
                return name;
        }

        return DeviceNameUtil.BuildDefaultDeviceName(deviceId);
    }


    private static Task<bool> IsNameTakenAsync(AppDbContext db, Guid userId, string name, Guid exceptDeviceId, CancellationToken ct)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return db.UserDevices.AsNoTracking().AnyAsync(ud =>
            ud.UserId == userId &&
            !ud.IsDeleted &&
            ud.DeviceId != exceptDeviceId &&
            ud.Name.ToUpper() == normalized, ct);
    }


    private static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();


    private static string BuildAddress(string host, int port)
    {
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return $"https://[{host}]:{port}";

        return $"https://{host}:{port}";
    }


    public void Dispose()
    {
        lock (_lock)
        {
            StopAdvertisingLocked();
            _currentSession = null;
        }
    }
}
