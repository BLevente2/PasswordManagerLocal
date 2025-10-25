using static PasswordManagerLocalBackend.Utils.DataValidationUtil;

namespace PasswordManagerLocalBackend.DTOs;

public sealed class RegistrationDTO
{
    public string Username { get; set; } = string.Empty;
    public byte[] Password { get; set; } = [];
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool RememberMe { get; set; } = false;


    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        if (!IsValidEmail(Email))
            errors.Add($"Email: {Email}");

        if (!IsValidUsername(Username))
            errors.Add($"Username: {Username}");

        if (!IsValidFirstName(FirstName))
            errors.Add($"FirstName: {FirstName}");

        if (!IsValidLastName(LastName))
            errors.Add($"LastName: {LastName}");

        if (!IsValidPassword(Password))
            errors.Add("Password");

        return errors.Count == 0;
    }
}