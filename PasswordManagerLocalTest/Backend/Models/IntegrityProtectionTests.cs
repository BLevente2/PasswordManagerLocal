using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models;
using PasswordManagerLocalBackend.Models.Encrypted;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Models;

[TestClass]
public sealed class IntegrityProtectionTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void SecurePassword_TamperingIsDetected()
    {
        using var password = CreateSecurePassword();
        password.GenerateIntegrityHash();

        MSTestAssert.IsTrue(password.IsIntegrityValid());

        password.Password[0] ^= 0x01;

        MSTestAssert.IsFalse(password.IsIntegrityValid());
        ExpectThrows<InvalidDataIntegrityException>(password.VerifyIntegrity);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void Device_GenerateIntegrityHashBindsSigningKeyAndDerivedHash()
    {
        var device = new Device
        {
            Id = Guid.Parse("D4DE4846-C997-45AF-A26B-9B3C345EAE5A"),
            PublicKey = Enumerable.Repeat((byte)1, 32).ToArray(),
            SignPublicKey = Enumerable.Repeat((byte)2, 32).ToArray(),
            TlsCertFingerprint = "AABBCCDD",
            DeviceType = DeviceType.WindowsPc,
            LastKnownHash = [3, 4, 5],
            LastSync = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            LastSeen = new DateTime(2026, 1, 2, 4, 4, 5, DateTimeKind.Utc),
            IsTrusted = true,
            LastModifiedAt = new DateTimeOffset(2026, 1, 2, 5, 4, 5, TimeSpan.Zero)
        };

        device.GenerateIntegrityHash();

        MSTestAssert.HasCount(32, device.SignPublicKeyHash);
        MSTestAssert.IsTrue(device.IsIntegrityValid());

        device.SignPublicKey[0] ^= 0x01;

        MSTestAssert.IsFalse(device.IsIntegrityValid());
        ExpectThrows<InvalidDataIntegrityException>(device.VerifyIntegrity);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void UserDevice_GenerateIntegrityHashCreatesAndBindsDeterministicModelId()
    {
        var link = new UserDevice
        {
            UserId = Guid.Parse("91A04F56-B972-4BFA-B020-6A2135988C83"),
            DeviceId = Guid.Parse("F530B2D3-AEF1-42F0-A623-F308CC3E258E"),
            IsSyncOn = true,
            LastModifiedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero)
        };

        link.GenerateIntegrityHash();
        var generatedModelId = link.ModelId;

        MSTestAssert.AreNotEqual(Guid.Empty, generatedModelId);
        MSTestAssert.IsTrue(link.IsIntegrityValid());

        link.UserId = Guid.NewGuid();

        MSTestAssert.IsFalse(link.IsIntegrityValid());
        ExpectThrows<InvalidDataIntegrityException>(link.VerifyIntegrity);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void LocalDeviceIdentity_PrivateKeyTamperingIsDetected()
    {
        var identity = new LocalDeviceIdentity
        {
            Id = Guid.NewGuid(),
            AgreementPrivateKeyBlob = Enumerable.Repeat((byte)1, 48).ToArray(),
            SignPrivateKeyBlob = Enumerable.Repeat((byte)2, 48).ToArray(),
            PFXCertificate = Enumerable.Repeat((byte)3, 128).ToArray(),
            DeviceType = DeviceType.AndroidMobile,
            IsSyncOn = true,
            CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)
        };
        identity.GenerateIntegrityHash();

        identity.AgreementPrivateKeyBlob[10] ^= 0x40;

        MSTestAssert.IsFalse(identity.IsIntegrityValid());
        ExpectThrows<InvalidDataIntegrityException>(identity.VerifyIntegrity);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void UserData_ChildIntegrityHashTamperingIsDetectedByParent()
    {
        using var data = CreateUserData();
        data.GenerateIntegrityHash();
        MSTestAssert.IsTrue(data.IsIntegrityValid());

        data.Passwords.IntegrityHash[0] ^= 0x20;

        MSTestAssert.IsFalse(data.IsIntegrityValid());
        ExpectThrows<InvalidDataIntegrityException>(data.VerifyIntegrity);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void SecurePassword_DisposeZeroesCallerObservableSecretBuffers()
    {
        var password = CreateSecurePassword();
        password.GenerateIntegrityHash();
        var passwordBuffer = password.Password;
        var integrityBuffer = password.IntegrityHash;

        password.Dispose();

        MSTestAssert.IsTrue(passwordBuffer.All(value => value == 0));
        MSTestAssert.IsTrue(integrityBuffer.All(value => value == 0));
        MSTestAssert.AreEqual(Guid.Empty, password.Id);
        MSTestAssert.AreEqual(string.Empty, password.Name);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void SecurePasswords_DisposeZeroesKeyAndContainedPasswords()
    {
        var child = CreateSecurePassword();
        child.GenerateIntegrityHash();
        var childSecret = child.Password;
        var key = Enumerable.Repeat((byte)9, 32).ToArray();
        var passwords = new SecurePasswords
        {
            PasswordKey = key,
            Passwords = [child]
        };
        passwords.GenerateIntegrityHash();

        passwords.Dispose();

        MSTestAssert.IsTrue(key.All(value => value == 0));
        MSTestAssert.IsTrue(childSecret.All(value => value == 0));
        MSTestAssert.IsEmpty(passwords.Passwords);
    }

    private static SecurePassword CreateSecurePassword() =>
        new()
        {
            Id = Guid.Parse("5E955FCD-D3BA-47C9-AB2E-13D0A6389624"),
            Name = "Email",
            Description = "Primary account",
            Color = "#FFFFD700",
            Password = [10, 20, 30, 40, 50],
            CreatedAt = new DateTime(2026, 1, 1, 1, 2, 3, DateTimeKind.Utc),
            LastUpdatedAt = new DateTime(2026, 1, 1, 2, 3, 4, DateTimeKind.Utc)
        };

    private static UserData CreateUserData()
    {
        var password = CreateSecurePassword();
        password.GenerateIntegrityHash();

        var passwords = new SecurePasswords
        {
            PasswordKey = Enumerable.Repeat((byte)4, 32).ToArray(),
            Passwords = [password]
        };
        passwords.GenerateIntegrityHash();

        var device = new UserDeviceData
        {
            Id = Guid.Parse("FD919B87-8D93-4996-887D-36FE0D791E97"),
            Name = "Laptop",
            LinkedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
        device.GenerateIntegrityHash();

        var devices = new SecureUserDevices { Devices = [device] };
        devices.GenerateIntegrityHash();

        return new UserData
        {
            UId = Guid.Parse("BD07BBD9-2ECB-4AC7-BD79-0E395BCF0414"),
            Username = "alice",
            FirstName = "Alice",
            LastName = "Example",
            Email = "alice@example.com",
            RegistrationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastLoginDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Passwords = passwords,
            UserDevices = devices
        };
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            MSTestAssert.Fail($"Expected exception: {typeof(TException).Name}");
        }
        catch (TException)
        {
        }
    }
}
