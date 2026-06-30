using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalBackend.Responses;

public sealed class UserProfileInfoResponse
{
    public Guid UId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; }
    public DateTime LastLoginDate { get; set; }
    public bool IsRememberMeEnabled { get; set; }



    public static UserProfileInfoResponse ConvertToUserProfileInfoResponse(
        UserData userData,
        GeneralUserData generalUserData,
        UserDevicesData userDevicesData,
        Guid currentDeviceId,
        bool isRememberMeEnabled = false) =>
        new UserProfileInfoResponse
        {
            UId = userData.UId,
            Username = generalUserData.Username,
            FirstName = generalUserData.FirstName,
            LastName = generalUserData.LastName,
            Email = generalUserData.Email,
            RegistrationDate = generalUserData.RegistrationDate,
            LastLoginDate = userDevicesData.Devices.FirstOrDefault(device => device.Id == currentDeviceId)?.LastLoginDate ?? DateTime.MinValue,
            IsRememberMeEnabled = isRememberMeEnabled
        };
}
