using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Abstractions.Repositories;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Sync.Enrollment;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;

internal sealed record PendingRenameScenario(
    Guid UserId,
    string OldUsername,
    string AdvertisedUsername,
    byte[] CanonicalHashBefore,
    SyncVersionStamp CanonicalVersionBefore,
    byte[] AdvertisedHash,
    SyncVersionStamp IncomingVersion,
    Guid SourceDeviceId,
    Guid SourceInstanceId,
    Guid RelayDeviceId,
    UserSnapshotReceiptResult Receipt);
