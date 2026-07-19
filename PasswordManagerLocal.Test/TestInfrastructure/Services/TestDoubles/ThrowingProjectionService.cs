using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSec.Cryptography;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Services;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using PasswordManagerLocal.Test.Fakes;
using PasswordManagerLocal.Test.TestInfrastructure;
using System.Security.Cryptography;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

namespace PasswordManagerLocal.Test.TestInfrastructure.Services.TestDoubles;

internal sealed class ThrowingProjectionService : IUserLoginIdentityProjectionService
{
    private readonly IUserLoginIdentityProjectionService _inner;

    public ThrowingProjectionService(IUserLoginIdentityProjectionService inner) => _inner = inner;

    public Task<UserLoginIdentityState> SetCanonicalAsync(User user, SyncVersionStamp generalUserDataVersion, CancellationToken ct = default) =>
        _inner.SetCanonicalAsync(user, generalUserDataVersion, ct);

    public Task<UserLoginIdentityState?> RecalculateAsync(Guid userId, CancellationToken ct = default) =>
        throw new InvalidOperationException("Injected projection persistence failure.");

    public Task<UserLoginIdentityState?> RecalculateUnderLifecycleAsync(Guid userId, CancellationToken ct = default) =>
        throw new InvalidOperationException("Injected projection persistence failure.");

    public Task<UserLoginIdentityMatchResult> FindByUsernameAsync(byte[] normalizedUsername, CancellationToken ct = default) =>
        _inner.FindByUsernameAsync(normalizedUsername, ct);
}
