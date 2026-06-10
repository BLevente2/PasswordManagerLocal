using Makaretu.Dns;
using Microsoft.Extensions.Hosting;
using PasswordManagerLocalBackend.Abstractions.Services;
using static PasswordManagerLocalBackend.Constants.SyncConstants;

namespace PasswordManagerLocalBackend.Services.Hosted;

public sealed class MdnsPublisherHostedService : ISyncControlledHostedService
{
    private readonly IDeviceIdentityService _identity;
    private ServiceDiscovery? _serviceDiscovery;
    private ServiceProfile? _profile;

    public MdnsPublisherHostedService(IDeviceIdentityService identity)
    {
        _identity = identity;
    }




    public int StartOrder => 30;




    public Task StartAsync(CancellationToken ct = default)
    {
        if (!_identity.IsSyncOn)
            return Task.CompletedTask;

        if (_serviceDiscovery is not null && _profile is not null)
            return Task.CompletedTask;

        _serviceDiscovery = new ServiceDiscovery();
        _profile = new ServiceProfile(BuildInstanceName(), MdnsServiceType, (ushort)SyncPort);

        _profile.AddProperty("deviceid", _identity.DeviceIdHex);
        _profile.AddProperty("deviceguid", _identity.LocalDeviceId.ToString("N"));
        _profile.AddProperty("signpub", Convert.ToHexString(_identity.SignPublicKey));
        _profile.AddProperty("agreepub", Convert.ToHexString(_identity.AgreementPublicKey));
        _profile.AddProperty("tlsfp", _identity.FingerprintHex);

        _serviceDiscovery.Advertise(_profile);
        _serviceDiscovery.Announce(_profile);

        return Task.CompletedTask;
    }


    public Task StopAsync(CancellationToken ct = default)
    {
        if (_serviceDiscovery is not null)
        {
            if (_profile is not null)
                _serviceDiscovery.Unadvertise(_profile);

            _serviceDiscovery.Dispose();
            _serviceDiscovery = null;
            _profile = null;
        }

        return Task.CompletedTask;
    }


    private string BuildInstanceName()
    {
        var id = _identity.DeviceIdHex;
        return $"pml-{id[..Math.Min(24, id.Length)]}";
    }
}
