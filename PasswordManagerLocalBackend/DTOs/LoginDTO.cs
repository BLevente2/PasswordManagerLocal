using static PasswordManagerLocalBackend.Utils.DataValidationUtil;

namespace PasswordManagerLocalBackend.DTOs;

public sealed class LoginDTO
{
    public string Username { get; set; } = string.Empty;
    public byte[] Password { get; set; } = [];
    public bool RememberMe { get; set; } = false;


    public bool Validate() => IsValidUsername(Username) && IsValidPassword(Password);
}