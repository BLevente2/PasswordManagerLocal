using PasswordManagerLocal.Backend.Models;
using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Responses;

public sealed class UserProfileInfoResponse
{
    public Guid UId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; }
    public string RegistrationTimeZoneId { get; set; } = string.Empty;
    public DeviceType RegistrationDeviceType { get; set; }
    public bool IsRememberMeEnabled { get; set; }



    public static UserProfileInfoResponse ConvertToUserProfileInfoResponse(
        UserData userData,
        GeneralUserData generalUserData,
        bool isRememberMeEnabled = false) =>
        new UserProfileInfoResponse
        {
            UId = userData.UId,
            Username = generalUserData.Username,
            FirstName = generalUserData.FirstName,
            LastName = generalUserData.LastName,
            Email = generalUserData.Email,
            RegistrationDate = UtcDateTimeUtil.ToUtc(generalUserData.RegistrationDate),
            RegistrationTimeZoneId = generalUserData.RegistrationTimeZoneId,
            RegistrationDeviceType = generalUserData.RegistrationDeviceType,
            IsRememberMeEnabled = isRememberMeEnabled
        };
}
