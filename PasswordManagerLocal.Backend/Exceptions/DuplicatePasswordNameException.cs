namespace PasswordManagerLocal.Backend.Exceptions;

public sealed class DuplicatePasswordNameException : Exception
{
    public string PasswordName { get; }

    public DuplicatePasswordNameException(string passwordName)
        : base($"A saved password with the name '{passwordName}' already exists.")
    {
        PasswordName = passwordName;
    }
}
