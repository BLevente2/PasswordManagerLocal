using Microsoft.Extensions.DependencyInjection;
using PasswordManagerLocal.Backend.Abstractions.Caching;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Enrollment;
using Google.Protobuf;
using PasswordManagerLocal.Backend.Constants;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Utils;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Constants.SyncConstants;
using PasswordManagerLocal.Backend.State;

namespace PasswordManagerLocal.Backend.Services;

public sealed class DeviceEnrollmentRegistrationService : IDeviceEnrollmentRegistrationService
{
    private readonly IDeviceIdentityService _identity;
    private readonly IDiscoveredDeviceEndpointCache _endpointCache;
    private readonly ILocalNetworkAddressService _networkAddresses;
    private readonly IDeviceEnrollmentLocalLinkService _localLinks;

    public DeviceEnrollmentRegistrationService(
        IDeviceIdentityService identity,
        IDiscoveredDeviceEndpointCache endpointCache,
        ILocalNetworkAddressService networkAddresses,
        IDeviceEnrollmentLocalLinkService localLinks)
    {
        _identity = identity;
        _endpointCache = endpointCache;
        _networkAddresses = networkAddresses;
        _localLinks = localLinks;
    }

    public async Task RegisterRemoteDeviceAsync(IServiceProvider services, Guid userId, EnrollmentEndpoint endpoint, CancellationToken ct)
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
        await _localLinks.EnsureLocalUserDeviceAsync(devices, localUserDevices, userId, ct);
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


    public async Task RejectIfPrimaryUserAlreadyLinkedToLocalDeviceAsync(IServiceProvider services, Guid userId, CancellationToken ct)
    {
        var localUserDevices = services.GetRequiredService<ILocalUserDeviceRepository>();
        var localLink = await localUserDevices.GetAsync(userId, ct);
        if (localLink is null)
            return;

        localLink.VerifyIntegrity();
        if (localLink.LocalDeviceIdentityId == _identity.LocalDeviceId)
            throw new DeviceEnrollmentException(DeviceEnrollmentErrorCode.NewDeviceRejected, "This profile is already linked to the local device.");
    }


    public async Task CacheIncomingEnrollmentSourceEndpointAsync(
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


    public async Task QueueInitialSyncAsync(IServiceProvider services, Guid userId, Guid newDeviceId, CancellationToken ct)
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
}
