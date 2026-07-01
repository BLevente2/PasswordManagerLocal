using static PasswordManagerLocalBackend.Utils.DataValidationUtil;
using static PasswordManagerLocalBackend.Constants.PasswordConstants;

namespace PasswordManagerLocalBackend.Requests;

public sealed class NewPasswordTagRequest
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = DefaultPasswordColor;

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        if (!IsValidPasswordTagName(Name))
            errors.Add("Name");

        if (string.IsNullOrWhiteSpace(Color) || !IsValidARGBColor(Color))
            errors.Add("Color");

        return errors.Count == 0;
    }
}
