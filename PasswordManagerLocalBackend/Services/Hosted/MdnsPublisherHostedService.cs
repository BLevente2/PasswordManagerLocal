using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class MdnsPublisherHostedService : IHostedService
{
    private readonly IDeviceIdentityService _identity;
    private ServiceDiscovery? _sd;
    private ServiceProfile? _profile;

    public MdnsPublisherHostedService(IDeviceIdentityService identity)
    {
        _identity = identity;
    }



    public Task StartAsync(CancellationToken ct = default)
    {
        if (_sd is not null && _profile is not null)
            return Task.CompletedTask;

        _sd = new ServiceDiscovery();

        _profile = new ServiceProfile(_identity.DeviceIdHex, MdnsServiceType, (ushort)SyncPort);

        _profile.AddProperty("signpub", Convert.ToHexString(_identity.SignPublicKey));
        _profile.AddProperty("tlsfp", _identity.FingerprintHex);

        _sd.Advertise(_profile);
        _sd.Announce(_profile);
        return Task.CompletedTask;
    }



    public Task StopAsync(CancellationToken ct = default)
    {
        if (_sd != null)
        {
            if (_profile != null)
                _sd.Unadvertise(_profile);

            _sd.Dispose();
        }
        return Task.CompletedTask;
    }
}