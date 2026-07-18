using PasswordManagerLocal.Backend.Abstractions.Security;
using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Backend.Services;

public class RememberMeService : IRememberMeService
{
    private readonly ITokenService _tokens;
    private readonly IKeyVaultService _keys;
    private readonly IKeyProtector _protector;
    private readonly IUserLookupService _lookup;
    private readonly IUserSessionService _sessions;
    private readonly IUserDataWriterService _writer;
    private readonly IUserDataReaderService _reader;
    private readonly IDeviceIdentityService _identity;
    private readonly IUserSnapshotMergeCoordinator _snapshotMerge;
    private readonly ISyncVersionClockService _versionClock;
    private readonly IUserTombstoneGarbageCollector? _garbageCollector;

    public RememberMeService(
        ITokenService tokens,
        IKeyVaultService keys,
        IKeyProtector protector,
        IUserLookupService lookup,
        IUserSessionService sessions,
        IUserDataWriterService writer,
        IUserDataReaderService reader,
        IDeviceIdentityService identity,
        IUserSnapshotMergeCoordinator snapshotMerge,
        ISyncVersionClockService versionClock,
        IUserTombstoneGarbageCollector? garbageCollector = null)
    {
        _tokens = tokens;
        _keys = keys;
        _protector = protector;
        _lookup = lookup;
        _sessions = sessions;
        _writer = writer;
        _reader = reader;
        _identity = identity;
        _snapshotMerge = snapshotMerge;
        _versionClock = versionClock;
        _garbageCollector = garbageCollector;
    }



    public async Task<IReadOnlyList<Guid>> InicializeAllRememberMeAsync(CancellationToken ct = default)
    {
        var initializedTokens = new List<Guid>();

        var usersEnabledRM = await _lookup.GetAndVerifyRememberMeEnabledUsersAsync(ct);
        if (usersEnabledRM.Count == 0)
            return initializedTokens;

        foreach (var user in usersEnabledRM)
        {
            var token = await TryInitializeRememberedUserAsync(user, ct);
            if (token is not null)
                initializedTokens.Add(token.Value);
        }

        return initializedTokens;
    }


    public async Task<Guid> InitializeRememberMeSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _lookup.GetAndVerifyUserByUidAsync(userId, ct);
        var token = await TryInitializeRememberedUserAsync(user, ct);

        if (token is null)
            throw new InvalidTokenException();

        return token.Value;
    }


    public async Task SetRememberMeAsync(Guid token, bool rememberMe, CancellationToken ct = default)
    {
        var user = await _lookup.GetAndVerifyUserAsync(token, ct);
        using var key = _sessions.GetEncryptionKeyFromToken(token);

        SetRememberMe(user, rememberMe, key);
        await _writer.UpdateUserAsync(user, ct);
    }


    public void SetRememberMe(User user, bool rememberMe, EncryptionKey key)
    {
        var isRememberMeCurrently = user.SavedKey is not null;

        if (!rememberMe)
        {
            if (!isRememberMeCurrently)
                return;

            CryptographicOperations.ZeroMemory(user.SavedKey);
            user.SavedKey = null;
            return;
        }

        var raw = key.ExportCopy();
        try
        {
            user.SavedKey = _protector.Protect(raw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }


    private async Task<Guid?> TryInitializeRememberedUserAsync(User user, CancellationToken ct)
    {
        if (user.SavedKey is null)
            return null;

        byte[]? rawKey = null;

        try
        {
            rawKey = _protector.Unprotect(user.SavedKey);
            using var key = EncryptionKey.FromRaw(rawKey);
            await _snapshotMerge.TryMergePendingAsync(user.UId, key, ct);
            if (_garbageCollector is not null)
            {
                try
                {
                    await _garbageCollector.CollectAsync(user.UId, key, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Remember Me unlock remains available when maintenance is conservatively blocked.
                }
            }
            user = await _lookup.GetAndVerifyUserByUidAsync(user.UId, ct);
            var bundle = await _reader.GetAndVerifyUserDataBundleAsync(user, key, ct);
            UserDeviceLoginUtil.UpdateCurrentDeviceLastLoginDate(
                bundle.UserDevicesData,
                _identity.LocalDeviceId,
                DateTimeOffset.UtcNow,
                _versionClock.Next());
            await _writer.UpdateUserDataBundleAsync(
                bundle,
                user,
                key,
                UserDataBlobKind.Devices,
                true,
                ct);

            var token = _tokens.Issue(user.UId);
            _keys.SetUserKey(token, key);
            _keys.SetUserBlobKeys(token, bundle.UserData);
            return token;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            await DisableBrokenRememberMeAsync(user, ct);
            return null;
        }
        finally
        {
            if (rawKey is not null)
                CryptographicOperations.ZeroMemory(rawKey);
        }
    }


    private async Task DisableBrokenRememberMeAsync(User user, CancellationToken ct)
    {
        try
        {
            if (user.SavedKey is not null)
                CryptographicOperations.ZeroMemory(user.SavedKey);

            user.SavedKey = null;
            await _writer.UpdateUserAsync(user, ct);
        }
        catch
        {
        }
    }
}
