using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Projections;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using static PasswordManagerLocal.Backend.Constants.DataLengthConstants;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserDevicesDataMergeService : IUserDevicesDataMergeService
{
    public bool Merge(UserDevicesData local, UserDevicesData incoming)
    {
        var changed = false;
        var localDevices = local.Devices.ToDictionary(device => device.Id);
        var incomingDevices = incoming.Devices.ToDictionary(device => device.Id);
        var localDeleted = local.DeletedDevices.ToDictionary(deleted => deleted.Id);
        var incomingDeleted = incoming.DeletedDevices.ToDictionary(deleted => deleted.Id);
        var ids = localDevices.Keys
            .Concat(incomingDevices.Keys)
            .Concat(localDeleted.Keys)
            .Concat(incomingDeleted.Keys)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var mergedDevices = new List<UserDeviceData>();
        var mergedDeleted = new List<DeletedUserDeviceData>();
        foreach (var id in ids)
        {
            localDevices.TryGetValue(id, out var localDevice);
            incomingDevices.TryGetValue(id, out var incomingDevice);
            localDeleted.TryGetValue(id, out var localDeletion);
            incomingDeleted.TryGetValue(id, out var incomingDeletion);

            var newestDevice = NewerDevice(localDevice, incomingDevice);
            var newestDeletion = NewerDeletedDevice(localDeletion, incomingDeletion);
            var deviceTime = newestDevice?.LastUpdatedAt ?? DateTimeOffset.MinValue;
            var deletionTime = newestDeletion?.DeletedAt ?? DateTimeOffset.MinValue;

            if (newestDeletion is not null && deletionTime >= deviceTime)
            {
                mergedDeleted.Add(newestDeletion);
                if (localDevice is not null || !ReferenceEquals(localDeletion, newestDeletion))
                    changed = true;
                continue;
            }

            if (newestDevice is not null)
            {
                if (localDevice is not null && incomingDevice is not null)
                {
                    var newestLogin = localDevice.LastLoginDate >= incomingDevice.LastLoginDate
                        ? localDevice.LastLoginDate
                        : incomingDevice.LastLoginDate;
                    if (newestDevice.LastLoginDate != newestLogin)
                    {
                        newestDevice.LastLoginDate = newestLogin;
                        changed = true;
                    }
                }

                mergedDevices.Add(newestDevice);
                if (!ReferenceEquals(localDevice, newestDevice) || localDeletion is not null)
                    changed = true;
            }
        }

        changed |= local.Devices.Count != mergedDevices.Count || local.DeletedDevices.Count != mergedDeleted.Count;
        if (!changed)
            return TombstoneCleanupUtil.EnforceDeletedUserDeviceTombstoneLimit(local.DeletedDevices);

        DisposeItemsNotKept(local.Devices, mergedDevices);
        DisposeItemsNotKept(local.DeletedDevices, mergedDeleted);
        local.Devices = ResolveDuplicateDeviceNamesForSync(mergedDevices);
        local.DeletedDevices = mergedDeleted.OrderBy(deleted => deleted.DeletedAt).ThenBy(deleted => deleted.Id).ToList();
        TombstoneCleanupUtil.EnforceDeletedUserDeviceTombstoneLimit(local.DeletedDevices);
        return true;
    }


    private UserDeviceData? NewerDevice(UserDeviceData? first, UserDeviceData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.LastUpdatedAt > first.LastUpdatedAt)
            return second;
        return first;
    }


    private DeletedUserDeviceData? NewerDeletedDevice(DeletedUserDeviceData? first, DeletedUserDeviceData? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;
        if (second.DeletedAt > first.DeletedAt)
            return second;
        return first;
    }


    private List<UserDeviceData> ResolveDuplicateDeviceNamesForSync(List<UserDeviceData> devices)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices.OrderByDescending(device => device.LastUpdatedAt).ThenBy(device => device.Id))
        {
            var baseName = string.IsNullOrWhiteSpace(device.Name) ? DeviceNameUtil.BuildDefaultDeviceName(device.Id) : device.Name.Trim();
            device.Name = BuildUniqueDeviceNameForSync(baseName, usedNames, device.Id);
            usedNames.Add(device.Name);
        }

        return devices.OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase).ThenBy(device => device.Id).ToList();
    }


    private string BuildUniqueDeviceNameForSync(string requestedName, HashSet<string> usedNames, Guid deviceId)
    {
        var baseName = requestedName.Trim();
        if (baseName.Length == 0)
            baseName = DeviceNameUtil.BuildDefaultDeviceName(deviceId);

        if (baseName.Length > UserDeviceNameMaxLength)
            baseName = baseName[..UserDeviceNameMaxLength];

        if (!usedNames.Contains(baseName))
            return baseName;

        var suffixSeed = deviceId.ToString("N")[..6];
        for (var i = 2; i < 100; i++)
        {
            var suffix = $"-{suffixSeed}-{i}";
            var prefixLength = Math.Max(1, UserDeviceNameMaxLength - suffix.Length);
            var candidate = baseName[..Math.Min(baseName.Length, prefixLength)] + suffix;
            if (!usedNames.Contains(candidate))
                return candidate;
        }

        return deviceId.ToString("N")[..UserDeviceNameMaxLength];
    }


    private void DisposeItemsNotKept<T>(IEnumerable<T> currentItems, IReadOnlyCollection<T> keptItems) where T : class, IDisposable
    {
        foreach (var current in currentItems)
        {
            if (!keptItems.Any(kept => ReferenceEquals(kept, current)))
                current.Dispose();
        }
    }
}
