using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Services;
using System.Security.Cryptography;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class PasswordServiceTests
{
    private static UserPasswordsData CreateEmptyPasswords()
    {
        var passwords = new UserPasswordsData();
        passwords.PasswordKey = RandomNumberGenerator.GetBytes(32);
        passwords.GenerateIntegrityHash();
        return passwords;
    }

    [TestMethod]
    public async Task AddNewPassword_Valid_Works()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        var req = new NewPasswordRequest
        {
            Name = "Test",
            Description = "Desc",
            Color = "#FFFFFFFF",
            Password = Encoding.UTF8.GetBytes("secret")
        };

        await service.AddNewPassword(req, passwords);

        MSTestAssert.HasCount(1, passwords.Passwords);
        passwords.VerifyIntegrity();
    }

    [TestMethod]
    public async Task AddNewPassword_InvalidInput_Throws()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        var req = new NewPasswordRequest();

        await ExpectThrowsAsync<InvalidInputException>(async () =>
        {
            await service.AddNewPassword(req, passwords);
        });
    }


    [TestMethod]
    public async Task AddNewPassword_DuplicateName_Throws()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        await service.AddNewPassword(new NewPasswordRequest
        {
            Name = "Email",
            Password = Encoding.UTF8.GetBytes("first")
        }, passwords);

        await ExpectThrowsAsync<DuplicatePasswordNameException>(async () =>
        {
            await service.AddNewPassword(new NewPasswordRequest
            {
                Name = " email ",
                Password = Encoding.UTF8.GetBytes("second")
            }, passwords);
        });

        MSTestAssert.HasCount(1, passwords.Passwords);
    }

    [TestMethod]
    public async Task UpdatePassword_DuplicateName_Throws()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        await service.AddNewPassword(new NewPasswordRequest
        {
            Name = "Email",
            Password = Encoding.UTF8.GetBytes("first")
        }, passwords);

        await service.AddNewPassword(new NewPasswordRequest
        {
            Name = "Bank",
            Password = Encoding.UTF8.GetBytes("second")
        }, passwords);

        var bankId = passwords.Passwords.Single(password => password.Name == "Bank").Id;

        await ExpectThrowsAsync<DuplicatePasswordNameException>(async () =>
        {
            await service.UpdatePasswordAsync(new UpdatePasswordRequest
            {
                Id = bankId,
                Name = "email"
            }, passwords);
        });

        MSTestAssert.AreEqual("Bank", passwords.Passwords.Single(password => password.Id == bankId).Name);
    }

    [TestMethod]
    public async Task GetUnsecurePassword_Roundtrip_Works()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        var raw = Encoding.UTF8.GetBytes("supersecret");

        await service.AddNewPassword(new NewPasswordRequest
        {
            Name = "Test",
            Password = raw
        }, passwords);

        var id = passwords.Passwords[0].Id;
        var decrypted = await service.GetUnsecurePasswordAsync(id, passwords);

        CollectionAssert.AreEqual(raw, decrypted);
    }

    [TestMethod]
    public void RemovePassword_Existing_Works()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        var pw = new SecurePassword
        {
            Id = Guid.NewGuid(),
            Name = "A",
            Password = Encoding.UTF8.GetBytes("x")
        };
        pw.GenerateIntegrityHash();

        passwords.Passwords.Add(pw);
        passwords.GenerateIntegrityHash();
        var passwordId = pw.Id;

        service.RemovePassword(passwordId, passwords);

        MSTestAssert.IsEmpty(passwords.Passwords);
        MSTestAssert.HasCount(1, passwords.DeletedPasswords);
        MSTestAssert.AreEqual(passwordId, passwords.DeletedPasswords[0].Id);
        passwords.VerifyIntegrity();
    }

    [TestMethod]
    public void RemovePassword_NonExisting_Throws()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        ExpectThrows<PasswordNotFoundException>(() =>
        {
            service.RemovePassword(Guid.NewGuid(), passwords);
        });
    }

    [TestMethod]
    public async Task UpdatePassword_ChangesFields()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        await service.AddNewPassword(new NewPasswordRequest
        {
            Name = "Old",
            Password = Encoding.UTF8.GetBytes("oldpw")
        }, passwords);

        var id = passwords.Passwords[0].Id;

        await service.UpdatePasswordAsync(new UpdatePasswordRequest
        {
            Id = id,
            Name = "New",
            Password = Encoding.UTF8.GetBytes("newpw")
        }, passwords);

        var decrypted = await service.GetUnsecurePasswordAsync(id, passwords);

        MSTestAssert.AreEqual("New", passwords.Passwords[0].Name);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("newpw"), decrypted);
    }


    [TestMethod]
    public async Task UpdatePassword_AllowsClearingDescription()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        await service.AddNewPassword(new NewPasswordRequest
        {
            Name = "Email",
            Description = "Remove me",
            Password = Encoding.UTF8.GetBytes("secret")
        }, passwords);

        var id = passwords.Passwords[0].Id;

        await service.UpdatePasswordAsync(new UpdatePasswordRequest
        {
            Id = id,
            Description = string.Empty
        }, passwords);

        MSTestAssert.AreEqual(string.Empty, passwords.Passwords[0].Description);
        passwords.VerifyIntegrity();
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public async Task GetUnsecurePassword_WhenStoredEntryWasTampered_ThrowsIntegrityException()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        await service.AddNewPassword(new NewPasswordRequest
        {
            Name = "Protected",
            Password = Encoding.UTF8.GetBytes("secret")
        }, passwords);

        var stored = passwords.Passwords.Single();
        stored.Password[0] ^= 0x01;

        await ExpectThrowsAsync<InvalidDataIntegrityException>(() =>
            service.GetUnsecurePasswordAsync(stored.Id, passwords));
    }


    [TestMethod]
    public async Task EncryptDecrypt_Roundtrip_Works()
    {
        var service = new PasswordService();
        var passwords = CreateEmptyPasswords();

        var raw = Encoding.UTF8.GetBytes("abc");

        var enc = await service.EncryptPasswordAsync(raw, passwords);
        var dec = await service.DecryptPasswordAsync(enc, passwords);

        CollectionAssert.AreEqual(raw, dec);
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

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try
        {
            await action();
            MSTestAssert.Fail($"Expected exception: {typeof(TException).Name}");
        }
        catch (TException)
        {
        }
    }
}