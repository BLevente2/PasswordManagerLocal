using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using PasswordManagerLocal.Backend.Sync.Discovery;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

internal sealed record DeviceSyncTaskSetup(
    DeviceSyncTaskService Service,
    ServiceProvider Provider,
    FakeSyncQueueRepository Queue,
    FakeUnitOfWork UnitOfWork,
    FakeSyncTransportClientService Transport,
    DiscoveredDeviceEndpointRegistry EndpointRegistry,
    FakeDeviceIdentityService Identity,
    Device Device,
    DiscoveredDeviceEndpoint Endpoint) : IDisposable
{
    public void Dispose()
    {
        Service.Dispose();
        Provider.Dispose();
    }
}
