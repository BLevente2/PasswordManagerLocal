using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

internal sealed record SeededUserRoutes(
    User User,
    IReadOnlyList<Device> EnabledRemotes,
    IReadOnlyList<Device> DisabledRemotes,
    FakeDeviceIdentityService Identity);
