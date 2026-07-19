using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Abstractions.Persistence;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

internal sealed record ValidDeltaSetup(
    NetworkDelta Delta,
    DeviceIdentityService Recipient,
    ServiceProvider SenderProvider,
    ServiceProvider RecipientProvider) : IDisposable
{
    public void Dispose()
    {
        SenderProvider.Dispose();
        RecipientProvider.Dispose();
    }
}
