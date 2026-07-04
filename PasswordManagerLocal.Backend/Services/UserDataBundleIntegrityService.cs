using PasswordManagerLocal.Backend.Abstractions.Services;
using PasswordManagerLocal.Backend.Exceptions;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Security;
using System.Security.Cryptography;

namespace PasswordManagerLocal.Backend.Services;

public sealed class UserDataBundleIntegrityService : IUserDataBundleIntegrityService
{
    public void VerifyUserData(UserData userData) =>
        userData.VerifyIntegrity();


    public void VerifyGeneralUserData(GeneralUserData generalUserData) =>
        generalUserData.VerifyIntegrity();


    public void VerifyUserPasswordsData(UserPasswordsData userPasswordsData)
    {
        userPasswordsData.VerifyIntegrity();
        VerifyPasswordChildren(userPasswordsData);
    }


    public void VerifyUserDevicesData(UserDevicesData userDevicesData)
    {
        userDevicesData.VerifyIntegrity();
        VerifyDeviceChildren(userDevicesData);
    }


    public void VerifyBundleLinks(UserDataBundle bundle)
    {
        VerifyStoredChildHash(
            bundle.UserData.GeneralUserDataIntegrityHash,
            bundle.GeneralUserData.IntegrityHash,
            typeof(GeneralUserData));
        VerifyStoredChildHash(
            bundle.UserData.UserPasswordsDataIntegrityHash,
            bundle.UserPasswordsData.IntegrityHash,
            typeof(UserPasswordsData));
        VerifyStoredChildHash(
            bundle.UserData.UserDevicesDataIntegrityHash,
            bundle.UserDevicesData.IntegrityHash,
            typeof(UserDevicesData));
    }


    public void VerifyUntrustedBundle(UserDataBundle bundle)
    {
        VerifyUserData(bundle.UserData);
        VerifyGeneralUserData(bundle.GeneralUserData);
        VerifyUserPasswordsData(bundle.UserPasswordsData);
        VerifyUserDevicesData(bundle.UserDevicesData);
        VerifyBundleLinks(bundle);
    }


    public void RebuildInitialIntegrity(UserDataBundle bundle)
    {
        bundle.GeneralUserData.GenerateIntegrityHash();
        RebuildUserPasswordsDataIntegrity(bundle.UserPasswordsData);
        RebuildUserDevicesDataIntegrity(bundle.UserDevicesData);
        CopyChildHashesToUserData(bundle, UserDataBlobKind.All);
        bundle.UserData.GenerateIntegrityHash();
        VerifyBundleLinks(bundle);
    }


    public void UpdateModifiedBlobIntegrity(UserDataBundle bundle, UserDataBlobKind modifiedBlobs)
    {
        if (modifiedBlobs.HasFlag(UserDataBlobKind.General))
            bundle.GeneralUserData.GenerateIntegrityHash();

        if (modifiedBlobs.HasFlag(UserDataBlobKind.Passwords))
        {
            VerifyPasswordChildren(bundle.UserPasswordsData);
            bundle.UserPasswordsData.GenerateIntegrityHash();
        }

        if (modifiedBlobs.HasFlag(UserDataBlobKind.Devices))
        {
            VerifyDeviceChildren(bundle.UserDevicesData);
            bundle.UserDevicesData.GenerateIntegrityHash();
        }

        CopyChildHashesToUserData(bundle, modifiedBlobs);
        bundle.UserData.GenerateIntegrityHash();
        VerifyBundleLinks(bundle);
    }


    public void RebuildModifiedBlobIntegrity(UserDataBundle bundle, UserDataBlobKind modifiedBlobs)
    {
        if (modifiedBlobs.HasFlag(UserDataBlobKind.General))
            bundle.GeneralUserData.GenerateIntegrityHash();

        if (modifiedBlobs.HasFlag(UserDataBlobKind.Passwords))
            RebuildUserPasswordsDataIntegrity(bundle.UserPasswordsData);

        if (modifiedBlobs.HasFlag(UserDataBlobKind.Devices))
            RebuildUserDevicesDataIntegrity(bundle.UserDevicesData);

        CopyChildHashesToUserData(bundle, modifiedBlobs);
        bundle.UserData.GenerateIntegrityHash();
        VerifyBundleLinks(bundle);
    }


    private void RebuildUserPasswordsDataIntegrity(UserPasswordsData userPasswordsData)
    {
        foreach (var password in userPasswordsData.Passwords)
            password.GenerateIntegrityHash();
        foreach (var deleted in userPasswordsData.DeletedPasswords)
            deleted.GenerateIntegrityHash();
        foreach (var color in userPasswordsData.CustomColors)
            color.GenerateIntegrityHash();
        foreach (var deleted in userPasswordsData.DeletedCustomColors)
            deleted.GenerateIntegrityHash();
        foreach (var tag in userPasswordsData.Tags)
            tag.GenerateIntegrityHash();
        foreach (var deleted in userPasswordsData.DeletedTags)
            deleted.GenerateIntegrityHash();

        userPasswordsData.GenerateIntegrityHash();
    }


    private void RebuildUserDevicesDataIntegrity(UserDevicesData userDevicesData)
    {
        foreach (var device in userDevicesData.Devices)
            device.GenerateIntegrityHash();
        foreach (var deleted in userDevicesData.DeletedDevices)
            deleted.GenerateIntegrityHash();

        userDevicesData.GenerateIntegrityHash();
    }


    private void VerifyPasswordChildren(UserPasswordsData userPasswordsData)
    {
        foreach (var password in userPasswordsData.Passwords)
            password.VerifyIntegrity();
        foreach (var deleted in userPasswordsData.DeletedPasswords)
            deleted.VerifyIntegrity();
        foreach (var color in userPasswordsData.CustomColors)
            color.VerifyIntegrity();
        foreach (var deleted in userPasswordsData.DeletedCustomColors)
            deleted.VerifyIntegrity();
        foreach (var tag in userPasswordsData.Tags)
            tag.VerifyIntegrity();
        foreach (var deleted in userPasswordsData.DeletedTags)
            deleted.VerifyIntegrity();
    }


    private void VerifyDeviceChildren(UserDevicesData userDevicesData)
    {
        foreach (var device in userDevicesData.Devices)
            device.VerifyIntegrity();
        foreach (var deleted in userDevicesData.DeletedDevices)
            deleted.VerifyIntegrity();
    }


    private void CopyChildHashesToUserData(UserDataBundle bundle, UserDataBlobKind modifiedBlobs)
    {
        if (modifiedBlobs.HasFlag(UserDataBlobKind.General))
            bundle.UserData.GeneralUserDataIntegrityHash = ReplaceHash(
                bundle.UserData.GeneralUserDataIntegrityHash,
                bundle.GeneralUserData.IntegrityHash);

        if (modifiedBlobs.HasFlag(UserDataBlobKind.Passwords))
            bundle.UserData.UserPasswordsDataIntegrityHash = ReplaceHash(
                bundle.UserData.UserPasswordsDataIntegrityHash,
                bundle.UserPasswordsData.IntegrityHash);

        if (modifiedBlobs.HasFlag(UserDataBlobKind.Devices))
            bundle.UserData.UserDevicesDataIntegrityHash = ReplaceHash(
                bundle.UserData.UserDevicesDataIntegrityHash,
                bundle.UserDevicesData.IntegrityHash);
    }


    private byte[] ReplaceHash(byte[] currentHash, byte[] sourceHash)
    {
        CryptographicOperations.ZeroMemory(currentHash);
        return sourceHash.ToArray();
    }


    private void VerifyStoredChildHash(byte[] expected, byte[] actual, Type type)
    {
        if (expected.Length != Hashing.SHA256HashSizeInBytes ||
            actual.Length != Hashing.SHA256HashSizeInBytes ||
            !Hashing.Verify(expected, actual))
            throw new InvalidDataIntegrityException(type);
    }
}
