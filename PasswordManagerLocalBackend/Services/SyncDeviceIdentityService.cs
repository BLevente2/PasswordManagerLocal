using PasswordManagerLocalBackend.Abstractions.Services;
using PasswordManagerLocalBackend.Models;
using System;
using System.Collections.Generic;
using System.Threading;

namespace PasswordManagerLocalBackend.Services;

public sealed class SyncDeviceIdentityService : ISyncDeviceIdentityService, IDisposable
{
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<Guid, Device> _devicesById = new();
    private readonly Dictionary<string, Guid> _deviceIdByFingerprint = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAdd(Device device)
    {
        if (device is null) return false;

        var id = device.Id;
        if (id == Guid.Empty) return false;

        var fingerprint = NormalizeFingerprint(device.TlsCertFingerprint);
        if (fingerprint.Length == 0) return false;

        _rwLock.EnterWriteLock();
        try
        {
            if (_devicesById.ContainsKey(id)) return false;
            if (_deviceIdByFingerprint.ContainsKey(fingerprint)) return false;

            _devicesById[id] = device;
            _deviceIdByFingerprint[fingerprint] = id;
            return true;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public int TryAdd(IReadOnlyList<Device> devices)
    {
        if (devices is null || devices.Count == 0) return 0;

        var added = 0;

        _rwLock.EnterWriteLock();
        try
        {
            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                if (device is null) continue;

                var id = device.Id;
                if (id == Guid.Empty) continue;

                var fingerprint = NormalizeFingerprint(device.TlsCertFingerprint);
                if (fingerprint.Length == 0) continue;

                if (_devicesById.ContainsKey(id)) continue;
                if (_deviceIdByFingerprint.ContainsKey(fingerprint)) continue;

                _devicesById[id] = device;
                _deviceIdByFingerprint[fingerprint] = id;
                added++;
            }

            return added;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public bool TryRemove(Device device)
    {
        if (device is null) return false;

        var id = device.Id;
        var fingerprint = NormalizeFingerprint(device.TlsCertFingerprint);

        _rwLock.EnterWriteLock();
        try
        {
            Guid idToRemove = Guid.Empty;

            if (id != Guid.Empty && _devicesById.ContainsKey(id))
            {
                idToRemove = id;
            }
            else if (fingerprint.Length != 0 && _deviceIdByFingerprint.TryGetValue(fingerprint, out var mappedId))
            {
                idToRemove = mappedId;
            }
            else
            {
                return false;
            }

            if (!_devicesById.TryGetValue(idToRemove, out var existing)) return false;

            _devicesById.Remove(idToRemove);

            var existingFingerprint = NormalizeFingerprint(existing.TlsCertFingerprint);
            if (existingFingerprint.Length != 0)
                _deviceIdByFingerprint.Remove(existingFingerprint);

            return true;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public int Count()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _devicesById.Count;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public bool Exists(Device device)
    {
        if (device is null) return false;

        var id = device.Id;
        var fingerprint = NormalizeFingerprint(device.TlsCertFingerprint);

        _rwLock.EnterReadLock();
        try
        {
            if (id != Guid.Empty && _devicesById.ContainsKey(id)) return true;
            if (fingerprint.Length != 0 && _deviceIdByFingerprint.ContainsKey(fingerprint)) return true;
            return false;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public bool ContainsFingerprint(string fingerprint)
    {
        var normalized = NormalizeFingerprint(fingerprint);
        if (normalized.Length == 0) return false;

        _rwLock.EnterReadLock();
        try
        {
            return _deviceIdByFingerprint.ContainsKey(normalized);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public bool TryGetByFingerprint(string fingerprint, out Device? device)
    {
        var normalized = NormalizeFingerprint(fingerprint);

        _rwLock.EnterReadLock();
        try
        {
            if (normalized.Length == 0)
            {
                device = null;
                return false;
            }

            if (!_deviceIdByFingerprint.TryGetValue(normalized, out var id))
            {
                device = null;
                return false;
            }

            if (_devicesById.TryGetValue(id, out var d))
            {
                device = d;
                return true;
            }

            device = null;
            return false;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public bool ContainsId(Guid deviceId)
    {
        if (deviceId == Guid.Empty) return false;

        _rwLock.EnterReadLock();
        try
        {
            return _devicesById.ContainsKey(deviceId);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public bool TryGetById(Guid deviceId, out Device? device)
    {
        _rwLock.EnterReadLock();
        try
        {
            return _devicesById.TryGetValue(deviceId, out device);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public bool IsEmpty()
    {
        _rwLock.EnterReadLock();
        try
        {
            return _devicesById.Count == 0;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    public void Clear()
    {
        _rwLock.EnterWriteLock();
        try
        {
            _devicesById.Clear();
            _deviceIdByFingerprint.Clear();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _rwLock.Dispose();
    }

    private static string NormalizeFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return string.Empty;

        var s = fingerprint.Trim();
        s = s.Replace(":", string.Empty);
        s = s.Replace(" ", string.Empty);
        return s.ToUpperInvariant();
    }
}