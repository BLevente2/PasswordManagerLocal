using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalBackend.Responses;

public sealed class UserDTO
{
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; }
    public DateTime LastLoginDate { get; set; }



    public static UserDTO ConvertToUserDTO(UserData user) => new UserDTO
    {
        Username = user.Username,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        RegistrationDate = user.RegistrationDate,
        LastLoginDate = user.LastLoginDate
    };
}