using static PasswordManagerLocal.Backend.Constants.PasswordConstants;

namespace PasswordManagerLocal.Backend.Requests;

public sealed class ExportPasswordsToUserRequest
{
    public Guid TargetToken { get; set; }
    public IReadOnlyList<Guid>? PasswordIds { get; set; } = [];

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        if (TargetToken == Guid.Empty)
            errors.Add("TargetToken");

        if (PasswordIds is null || PasswordIds.Count == 0 || PasswordIds.Count > MaxNumberOfPasswords || PasswordIds.Any(id => id == Guid.Empty))
            errors.Add("PasswordIds");
        else if (PasswordIds.Distinct().Count() != PasswordIds.Count)
            errors.Add("PasswordIds");

        return errors.Count == 0;
    }
}
