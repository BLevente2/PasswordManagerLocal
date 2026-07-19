using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

internal sealed record UserTombstoneGarbageCollectorMember(Guid DeviceId, Guid InstanceId);
