using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Services;

/// <summary>
/// Determines whether a persisted device record represents the current physical device.
/// </summary>
public sealed class LocalDeviceMatcherService : ILocalDeviceMatcherService
{
    private readonly IDeviceIdentityService _identity;

    public LocalDeviceMatcherService(IDeviceIdentityService identity)
    {
        _identity = identity;
    }

    public bool IsLocalDevice(Device device)
    {
        if (!_identity.IsInitialized)
            return false;

        if (device.Id == _identity.LocalDeviceId)
            return true;

        if (device.SignPublicKey.SequenceEqual(_identity.SignPublicKey))
            return true;

        return string.Equals(
            FingerprintUtil.NormalizeOrEmpty(device.TlsCertFingerprint),
            FingerprintUtil.NormalizeOrEmpty(_identity.FingerprintHex),
            StringComparison.OrdinalIgnoreCase);
    }
}
