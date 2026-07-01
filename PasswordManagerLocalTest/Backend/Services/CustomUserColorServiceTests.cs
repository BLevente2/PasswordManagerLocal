using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Exceptions;
using PasswordManagerLocalBackend.Models.Encrypted;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Services;
using System.Security.Cryptography;
using static PasswordManagerLocalBackend.Constants.PasswordConstants;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Services;

[TestClass]
public sealed class CustomUserColorServiceTests
{
    private static UserPasswordsData CreateEmptyPasswords()
    {
        var passwords = new UserPasswordsData();
        passwords.PasswordKey = RandomNumberGenerator.GetBytes(32);
        passwords.GenerateIntegrityHash();
        return passwords;
    }


    [TestMethod]
    public void AddCustomUserColor_ValidRequest_NormalizesAndPersists()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "  Personal  ",
            ColorCode = "#ffabcdef"
        }, passwords);

        MSTestAssert.HasCount(1, passwords.CustomColors);
        MSTestAssert.AreEqual("Personal", passwords.CustomColors[0].ColorName);
        MSTestAssert.AreEqual("#FFABCDEF", passwords.CustomColors[0].ColorCode);
        passwords.CustomColors[0].VerifyIntegrity();
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void AddCustomUserColor_InvalidRequest_ThrowsAndDoesNotModifyData()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();
        var originalHash = passwords.IntegrityHash.ToArray();

        ExpectThrows<InvalidInputException>(() =>
        {
            service.AddCustomUserColor(new NewCustomUserColorRequest
            {
                ColorName = "Useful",
                ColorCode = "not-a-color"
            }, passwords);
        });

        MSTestAssert.IsEmpty(passwords.CustomColors);
        CollectionAssert.AreEqual(originalHash, passwords.IntegrityHash);
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void AddCustomUserColor_DuplicateColorCode_ThrowsCaseInsensitively()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorCode = "#FF000001"
        }, passwords);

        ExpectThrows<DuplicateCustomUserColorCodeException>(() =>
        {
            service.AddCustomUserColor(new NewCustomUserColorRequest
            {
                ColorCode = "#ff000001"
            }, passwords);
        });

        MSTestAssert.HasCount(1, passwords.CustomColors);
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void AddCustomUserColor_DuplicateColorName_ThrowsCaseInsensitively()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Personal",
            ColorCode = "#FF000001"
        }, passwords);

        ExpectThrows<DuplicateCustomUserColorNameException>(() =>
        {
            service.AddCustomUserColor(new NewCustomUserColorRequest
            {
                ColorName = "  personal  ",
                ColorCode = "#FF000002"
            }, passwords);
        });

        MSTestAssert.HasCount(1, passwords.CustomColors);
        MSTestAssert.AreEqual("Personal", passwords.CustomColors[0].ColorName);
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void AddCustomUserColor_MultipleUnnamedColors_AreAllowed()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorCode = "#FF000001"
        }, passwords);
        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorCode = "#FF000002"
        }, passwords);

        MSTestAssert.HasCount(2, passwords.CustomColors);
        MSTestAssert.IsTrue(passwords.CustomColors.All(color => color.ColorName is null));
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void AddCustomUserColor_WhenLimitReached_ThrowsAndDoesNotModifyData()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        for (var i = 0; i < MaxNumberOfCustomUserColors; i++)
        {
            var color = new CustomUserColor
            {
                Id = Guid.NewGuid(),
                ColorName = $"Color {i}",
                ColorCode = $"#FF{i:X6}",
                LastUpdatedAt = DateTime.UtcNow
            };
            color.GenerateIntegrityHash();
            passwords.CustomColors.Add(color);
        }
        passwords.GenerateIntegrityHash();

        ExpectThrows<LimitReachedException>(() =>
        {
            service.AddCustomUserColor(new NewCustomUserColorRequest
            {
                ColorName = "Overflow",
                ColorCode = "#FFFFFFFF"
            }, passwords);
        });

        MSTestAssert.HasCount(MaxNumberOfCustomUserColors, passwords.CustomColors);
        MSTestAssert.IsFalse(passwords.CustomColors.Any(color => color.ColorName == "Overflow"));
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void UpdateCustomUserColor_ValidRequest_CanClearNameAndChangeColorCode()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Work",
            ColorCode = "#FF112233"
        }, passwords);

        var colorId = passwords.CustomColors[0].Id;
        service.UpdateCustomUserColor(new UpdateCustomUserColorRequest
        {
            Id = colorId,
            ClearColorName = true,
            ColorCode = "#ff445566"
        }, passwords);

        MSTestAssert.IsNull(passwords.CustomColors[0].ColorName);
        MSTestAssert.AreEqual("#FF445566", passwords.CustomColors[0].ColorCode);
        passwords.CustomColors[0].VerifyIntegrity();
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void UpdateCustomUserColor_DuplicateColorCode_ThrowsAndKeepsOriginalValues()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Primary",
            ColorCode = "#FF000001"
        }, passwords);
        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Secondary",
            ColorCode = "#FF000002"
        }, passwords);

        var secondary = passwords.CustomColors.Single(color => color.ColorName == "Secondary");
        var originalHash = passwords.IntegrityHash.ToArray();

        ExpectThrows<DuplicateCustomUserColorCodeException>(() =>
        {
            service.UpdateCustomUserColor(new UpdateCustomUserColorRequest
            {
                Id = secondary.Id,
                ColorCode = "#ff000001"
            }, passwords);
        });

        MSTestAssert.AreEqual("Secondary", secondary.ColorName);
        MSTestAssert.AreEqual("#FF000002", secondary.ColorCode);
        CollectionAssert.AreEqual(originalHash, passwords.IntegrityHash);
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void UpdateCustomUserColor_DuplicateColorName_ThrowsAndKeepsOriginalValues()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Primary",
            ColorCode = "#FF000001"
        }, passwords);
        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Secondary",
            ColorCode = "#FF000002"
        }, passwords);

        var secondary = passwords.CustomColors.Single(color => color.ColorName == "Secondary");
        var originalHash = passwords.IntegrityHash.ToArray();

        ExpectThrows<DuplicateCustomUserColorNameException>(() =>
        {
            service.UpdateCustomUserColor(new UpdateCustomUserColorRequest
            {
                Id = secondary.Id,
                ColorName = "  primary  "
            }, passwords);
        });

        MSTestAssert.AreEqual("Secondary", secondary.ColorName);
        MSTestAssert.AreEqual("#FF000002", secondary.ColorCode);
        CollectionAssert.AreEqual(originalHash, passwords.IntegrityHash);
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void UpdateCustomUserColor_InvalidRequest_ThrowsAndDoesNotModifyData()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Accent",
            ColorCode = "#FFABCDEF"
        }, passwords);

        var color = passwords.CustomColors[0];
        var originalHash = passwords.IntegrityHash.ToArray();

        ExpectThrows<InvalidInputException>(() =>
        {
            service.UpdateCustomUserColor(new UpdateCustomUserColorRequest
            {
                Id = color.Id,
                ColorCode = "#XYZ"
            }, passwords);
        });

        MSTestAssert.AreEqual("Accent", color.ColorName);
        MSTestAssert.AreEqual("#FFABCDEF", color.ColorCode);
        CollectionAssert.AreEqual(originalHash, passwords.IntegrityHash);
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void DeleteCustomUserColor_ExistingColor_RemovesAndCreatesTombstone()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Personal",
            ColorCode = "#FF123456"
        }, passwords);

        var colorId = passwords.CustomColors[0].Id;
        service.DeleteCustomUserColor(colorId, passwords);

        MSTestAssert.IsEmpty(passwords.CustomColors);
        MSTestAssert.HasCount(1, passwords.DeletedCustomColors);
        MSTestAssert.AreEqual(colorId, passwords.DeletedCustomColors[0].Id);
        passwords.DeletedCustomColors[0].VerifyIntegrity();
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void DeleteCustomUserColor_NonExistingColor_Throws()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        ExpectThrows<CustomUserColorNotFoundException>(() =>
        {
            service.DeleteCustomUserColor(Guid.NewGuid(), passwords);
        });

        MSTestAssert.IsEmpty(passwords.CustomColors);
        MSTestAssert.IsEmpty(passwords.DeletedCustomColors);
        passwords.VerifyIntegrity();
    }


    [TestMethod]
    public void ConvertToCustomUserColorInfoResponses_ReturnsStableSortedResponses()
    {
        var service = new CustomUserColorService();
        var passwords = CreateEmptyPasswords();

        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Work",
            ColorCode = "#FF000003"
        }, passwords);
        service.AddCustomUserColor(new NewCustomUserColorRequest
        {
            ColorName = "Personal",
            ColorCode = "#FF000002"
        }, passwords);

        var response = service.ConvertToCustomUserColorInfoResponses(passwords);

        MSTestAssert.HasCount(2, response);
        MSTestAssert.AreEqual("Personal", response[0].ColorName);
        MSTestAssert.AreEqual("Work", response[1].ColorName);
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
