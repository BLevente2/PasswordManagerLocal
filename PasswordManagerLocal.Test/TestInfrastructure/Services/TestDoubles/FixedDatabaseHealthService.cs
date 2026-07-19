using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using NSecKey = NSec.Cryptography.Key;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;
using System.Text.Json;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using PasswordManagerLocal.Test.TestInfrastructure.Services.Fixtures;
using PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

internal sealed class FixedDatabaseHealthService : IDatabaseHealthService
{
    private readonly DatabaseHealthCheckResult _result;

    public FixedDatabaseHealthService(bool healthy, string code) =>
        _result = new DatabaseHealthCheckResult(healthy, code);

    public Task<DatabaseHealthCheckResult> CheckAsync(CancellationToken ct = default) =>
        Task.FromResult(_result);
}
