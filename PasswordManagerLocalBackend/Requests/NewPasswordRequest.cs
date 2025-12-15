using static PasswordManagerLocalBackend.Utils.DataValidationUtil;

namespace PasswordManagerLocalBackend.Requests;

public sealed class NewPasswordRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#FFFFD700"; // Classic gold default color
    public byte[] Password { get; set; } = [];



    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        if (!IsValidPasswordName(Name))
            errors.Add("Name");

        if (!IsValidDescription(Description))
            errors.Add("Description");

        if (!IsValidARGBColor(Color))
            errors.Add("Color");

        if (!IsValidPassword(Password))
            errors.Add("Password");

        return errors.Count == 0;
    }
}