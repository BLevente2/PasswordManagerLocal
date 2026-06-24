using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocalBackend.Constants;
using PasswordManagerLocalBackend.Requests;
using PasswordManagerLocalBackend.Utils;
using System.Text;

using MSTestAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace PasswordManagerLocalTest.Backend.Utils;

[TestClass]
public sealed class DataValidationUtilTests
{
    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Security")]
    public void IsValidEmail_AcceptsConventionalAddressAndRejectsMissingOrInvalidDomainSeparators()
    {
        MSTestAssert.IsTrue(DataValidationUtil.IsValidEmail("user.name+tag@example-domain.com"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidEmail("user@examplecom"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidEmail("user@example#com"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidEmail("user@.com"));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void IsValidUsername_EnforcesAllowedCharactersAndTrimmedLengthBoundaries()
    {
        MSTestAssert.IsTrue(DataValidationUtil.IsValidUsername(" abc "));
        MSTestAssert.IsTrue(DataValidationUtil.IsValidUsername(new string('a', DataLengthConstants.UsernameMaxLength)));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidUsername("ab"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidUsername(new string('a', DataLengthConstants.UsernameMaxLength + 1)));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidUsername("user name"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidUsername("user!"));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void NameValidation_AcceptsUnicodeLettersAndRejectsDigitsOrPunctuation()
    {
        MSTestAssert.IsTrue(DataValidationUtil.IsValidFirstName("Árvíztűrő"));
        MSTestAssert.IsTrue(DataValidationUtil.IsValidLastName("Őz"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidFirstName("John2"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidLastName("Smith-Jones"));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void IsValidARGBColor_RequiresHashAndExactlyEightHexDigits()
    {
        MSTestAssert.IsTrue(DataValidationUtil.IsValidARGBColor("#00FF7FA0"));
        MSTestAssert.IsTrue(DataValidationUtil.IsValidARGBColor("#ffffffff"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidARGBColor("00FF7FA0"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidARGBColor("#FFF"));
        MSTestAssert.IsFalse(DataValidationUtil.IsValidARGBColor("#GGFF7FA0"));
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void RegistrationRequest_ReportsEveryInvalidFieldWithoutHidingLaterErrors()
    {
        var request = new RegistrationRequest
        {
            Username = "x",
            Password = [],
            FirstName = "123",
            LastName = "!",
            Email = "invalid"
        };

        var valid = request.Validate(out var errors);

        MSTestAssert.IsFalse(valid);
        CollectionAssert.AreEquivalent(
            new[] { "Email", "Username", "FirstName", "LastName", "Password" },
            errors);
    }

    [TestMethod]
    [TestCategory("Backend")]
    [TestCategory("Unit")]
    public void NewPasswordRequest_EnforcesAllConfiguredLengthBoundaries()
    {
        var request = new NewPasswordRequest
        {
            Name = new string('N', DataLengthConstants.PasswordNameMaxLength),
            Description = new string('D', DataLengthConstants.DescriptionMaxLength),
            Color = "#FF112233",
            Password = Encoding.UTF8.GetBytes("x")
        };

        MSTestAssert.IsTrue(request.Validate(out var validErrors));
        MSTestAssert.IsEmpty(validErrors);

        request.Name += "N";
        request.Description += "D";
        request.Password = new byte[DataLengthConstants.PasswordMaxLength + 1];

        MSTestAssert.IsFalse(request.Validate(out var invalidErrors));
        CollectionAssert.AreEquivalent(new[] { "Name", "Description", "Password" }, invalidErrors);
    }
}
