using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Text.Json;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

internal sealed class FixedCanonicalHealthService : IUserCanonicalHealthService
{
    private readonly CanonicalHealthResult _result;

    public FixedCanonicalHealthService(CanonicalHealthResult result) => _result = result;

    public Task<CanonicalHealthResult> VerifyAsync(
        User user,
        EncryptionKey? key,
        UserSyncKeyConfidence keyConfidence,
        bool recordFault,
        CancellationToken ct = default) => Task.FromResult(_result);

    public Task UpdateCheckpointAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteCheckpointAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
}
