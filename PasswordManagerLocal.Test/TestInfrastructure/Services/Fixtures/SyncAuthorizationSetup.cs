using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

internal sealed record SyncAuthorizationSetup(
    SyncAuthorizationService Service,
    FakeGroupRepository Groups,
    FakeUserDeviceRepository UserDevices,
    FakeLocalUserDeviceRepository LocalUsers,
    FakeDeviceIdentityService Identity);
