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



    public static UserProfileInfoResponse ConvertToUserProfileInfoResponse(UserData user, bool isRememberMeEnabled = false) =>
        new UserProfileInfoResponse
        {
            UId = user.UId,
            Username = user.Username,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            RegistrationDate = user.RegistrationDate,
            LastLoginDate = user.LastLoginDate,
            IsRememberMeEnabled = isRememberMeEnabled
        };
}