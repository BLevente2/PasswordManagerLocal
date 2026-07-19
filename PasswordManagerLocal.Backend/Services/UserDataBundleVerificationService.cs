using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using PasswordManagerLocal.Backend.Sync;
using PasswordManagerLocal.Backend.Utils;
using System.Security.Cryptography;
using System.Text.Json;
using static PasswordManagerLocal.Backend.Utils.DataCodec;

using PasswordManagerLocal.Backend.Internal.Recovery;
namespace PasswordManagerLocal.Backend.Services;

public sealed class UserDataBundleVerificationService : IUserDataBundleVerificationService
{
    private readonly IUserDataBundleIntegrityService _integrity;

    public UserDataBundleVerificationService(IUserDataBundleIntegrityService integrity) => _integrity = integrity;

    public Task<UserDataBundleVerificationResult> VerifyCanonicalAsync(
        User user,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        CancellationToken ct = default) =>
        VerifyUserAsync(user, key, keyConfidence, verifyLoginMetadata: true, ct);

    public async Task<UserDataBundleVerificationResult> VerifySnapshotAsync(
        UserSnapshotEnvelope snapshot,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        CancellationToken ct = default)
    {
        try
        {
            UserSnapshotEnvelopeUtil.ValidateStructureAndHash(snapshot);
        }
        catch (Exception ex) when (IsVerificationFailure(ex))
        {
            return Failed(UserDataVerificationState.AuthorizationFailure, UserDataBlobKind.None, keyConfidence, "snapshot-envelope-invalid");
        }

        var user = new User
        {
            UId = snapshot.User.UId,
            UsernameHash = snapshot.User.UsernameHash.ToArray(),
            UsernameSalt = snapshot.User.UsernameSalt.ToArray(),
            PasswordSalt = snapshot.User.PasswordSalt.ToArray(),
            EncryptedPayload = snapshot.User.EncryptedPayload.ToArray(),
            EncryptedGeneralUserDataPayload = snapshot.User.EncryptedGeneralUserDataPayload.ToArray(),
            EncryptedUserPasswordsDataPayload = snapshot.User.EncryptedUserPasswordsDataPayload.ToArray(),
            EncryptedUserDevicesDataPayload = snapshot.User.EncryptedUserDevicesDataPayload.ToArray(),
            UserDataLastModifiedAt = snapshot.User.UserDataLastModifiedAt,
            GeneralUserDataLastModifiedAt = snapshot.User.GeneralUserDataLastModifiedAt,
            UserPasswordsDataLastModifiedAt = snapshot.User.UserPasswordsDataLastModifiedAt,
            UserDevicesDataLastModifiedAt = snapshot.User.UserDevicesDataLastModifiedAt,
            LastModifiedAt = snapshot.CreatedAtUtc,
            KeyEpoch = snapshot.UserKeyEpoch,
            MembershipEpoch = snapshot.MembershipEpoch
        };
        user.SetGeneralUserDataVersion(snapshot.User.GeneralUserDataVersion);
        user.GenerateIntegrityHash();

        var result = await VerifyUserAsync(user, key, keyConfidence, verifyLoginMetadata: false, ct);
        if (!result.IsHealthy)
            return result;

        try
        {
            UserLoginIdentityMetadataUtil.Verify(
                snapshot.User.UsernameHash,
                snapshot.User.UsernameSalt,
                snapshot.User.GeneralUserDataVersion,
                result.VerifiedBundle!.GeneralUserData);
            return result;
        }
        catch (Exception ex) when (IsVerificationFailure(ex))
        {
            result.Dispose();
            return Failed(UserDataVerificationState.LoginMetadataFailure, UserDataBlobKind.General, keyConfidence, "snapshot-login-metadata-mismatch");
        }
    }

    private async Task<UserDataBundleVerificationResult> VerifyUserAsync(
        User user,
        EncryptionKey key,
        UserSyncKeyConfidence keyConfidence,
        bool verifyLoginMetadata,
        CancellationToken ct)
    {
        UserData? root = null;
        GeneralUserData? general = null;
        UserPasswordsData? passwords = null;
        UserDevicesData? devices = null;
        try
        {
            try
            {
                root = await DecryptDecompressDeserializeAsync(
                    user.EncryptedPayload,
                    key,
                    BackendJsonSerializerContext.Default.UserData,
                    ct: ct);
                if (root is null)
                    throw new UnauthorizedAccessException();
            }
            catch (Exception ex) when (IsDecryptFailure(ex))
            {
                return Failed(UserDataVerificationState.RootDecryptFailure, UserDataBlobKind.All, keyConfidence, "root-decrypt-failed");
            }

            try
            {
                _integrity.VerifyUserData(root);
            }
            catch (Exception ex) when (IsIntegrityFailure(ex))
            {
                return Failed(UserDataVerificationState.RootIntegrityFailure, UserDataBlobKind.All, keyConfidence, "root-integrity-failed");
            }

            var generalResult = await DecryptAndVerifyBlobAsync(
                user.EncryptedGeneralUserDataPayload,
                root.GeneralUserDataKey,
                BackendJsonSerializerContext.Default.GeneralUserData,
                _integrity.VerifyGeneralUserData,
                UserDataVerificationState.GeneralBlobFailure,
                UserDataBlobKind.General,
                "general",
                keyConfidence,
                ct);
            if (!generalResult.Success)
                return generalResult.Failure!;
            general = (GeneralUserData)generalResult.Value!;

            var passwordsResult = await DecryptAndVerifyBlobAsync(
                user.EncryptedUserPasswordsDataPayload,
                root.UserPasswordsDataKey,
                BackendJsonSerializerContext.Default.UserPasswordsData,
                _integrity.VerifyUserPasswordsData,
                UserDataVerificationState.PasswordsBlobFailure,
                UserDataBlobKind.Passwords,
                "passwords",
                keyConfidence,
                ct);
            if (!passwordsResult.Success)
                return passwordsResult.Failure!;
            passwords = (UserPasswordsData)passwordsResult.Value!;

            var devicesResult = await DecryptAndVerifyBlobAsync(
                user.EncryptedUserDevicesDataPayload,
                root.UserDevicesDataKey,
                BackendJsonSerializerContext.Default.UserDevicesData,
                _integrity.VerifyUserDevicesData,
                UserDataVerificationState.DevicesBlobFailure,
                UserDataBlobKind.Devices,
                "devices",
                keyConfidence,
                ct);
            if (!devicesResult.Success)
                return devicesResult.Failure!;
            devices = (UserDevicesData)devicesResult.Value!;

            var bundle = new UserDataBundle
            {
                UserData = root,
                GeneralUserData = general,
                UserPasswordsData = passwords,
                UserDevicesData = devices
            };
            root = null;
            general = null;
            passwords = null;
            devices = null;

            try
            {
                _integrity.VerifyBundleLinks(bundle);
            }
            catch (Exception ex) when (IsIntegrityFailure(ex))
            {
                bundle.Dispose();
                return Failed(UserDataVerificationState.BundleLinkFailure, UserDataBlobKind.All, keyConfidence, "bundle-link-failed");
            }

            if (verifyLoginMetadata)
            {
                try
                {
                    UserLoginIdentityMetadataUtil.Verify(user, bundle.GeneralUserData);
                }
                catch (Exception ex) when (IsVerificationFailure(ex))
                {
                    bundle.Dispose();
                    return Failed(UserDataVerificationState.LoginMetadataFailure, UserDataBlobKind.General, keyConfidence, "canonical-login-metadata-mismatch");
                }
            }

            return new UserDataBundleVerificationResult
            {
                State = UserDataVerificationState.Healthy,
                KeyConfidence = keyConfidence,
                VerifiedBundle = bundle,
                DiagnosticCode = "healthy"
            };
        }
        finally
        {
            root?.Dispose();
            general?.Dispose();
            passwords?.Dispose();
            devices?.Dispose();
        }
    }

    private async Task<BlobVerificationResult> DecryptAndVerifyBlobAsync<T>(
        byte[] encrypted,
        byte[] rawKey,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Action<T> verify,
        UserDataVerificationState failureState,
        UserDataBlobKind failedBlob,
        string diagnosticPrefix,
        UserSyncKeyConfidence keyConfidence,
        CancellationToken ct) where T : class, IDisposable
    {
        T? value;
        try
        {
            if (encrypted.Length == 0 || rawKey.Length == 0)
                throw new UnauthorizedAccessException();
            using var blobKey = EncryptionKey.FromRaw(rawKey);
            value = await DecryptDecompressDeserializeAsync(encrypted, blobKey, typeInfo, ct: ct);
            if (value is null)
                throw new UnauthorizedAccessException();
        }
        catch (Exception ex) when (IsDecryptFailure(ex))
        {
            return BlobVerificationResult.Failed(Failed(failureState, failedBlob, keyConfidence, $"{diagnosticPrefix}-decrypt-failed"));
        }

        try
        {
            verify(value);
            return BlobVerificationResult.Verified(value);
        }
        catch (Exception ex) when (IsIntegrityFailure(ex))
        {
            value.Dispose();
            return BlobVerificationResult.Failed(Failed(failureState, failedBlob, keyConfidence, $"{diagnosticPrefix}-integrity-failed"));
        }
    }

    private UserDataBundleVerificationResult Failed(
        UserDataVerificationState state,
        UserDataBlobKind blobs,
        UserSyncKeyConfidence confidence,
        string diagnosticCode) =>
        new()
        {
            State = state,
            FailedBlobs = blobs,
            KeyConfidence = confidence,
            DiagnosticCode = diagnosticCode
        };

    private bool IsDecryptFailure(Exception ex) =>
        ex is UnauthorizedAccessException or CryptographicException or InvalidDataException or JsonException or ArgumentException;

    private bool IsIntegrityFailure(Exception ex) =>
        ex is InvalidDataIntegrityException or InvalidDataException;

    private bool IsVerificationFailure(Exception ex) => IsDecryptFailure(ex) || IsIntegrityFailure(ex);

}
