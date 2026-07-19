using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Responses;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Test.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

internal sealed record EnrollmentServiceSetup(
    DeviceEnrollmentService Service,
    ServiceProvider Provider,
    FakeSyncRuntimeService Runtime) : IDisposable
{
    public void Dispose()
    {
        Service.Dispose();
        Provider.Dispose();
    }
}
