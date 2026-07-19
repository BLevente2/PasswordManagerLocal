using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;
using System.Security.Cryptography;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

internal sealed class ThrowingDeletionCleanupService : PasswordManagerLocal.Backend.Abstractions.Services.IUserAccountDeletionCleanupService
{
    public Task DeleteCanonicalAndPendingStateAsync(Guid userId, CancellationToken ct = default) =>
        throw new InvalidOperationException("Injected cleanup failure.");
}
