namespace PasswordManagerLocalBackend.Exceptions;

public class InvalidDataIntegrityException : Exception
{
    public Type ObjectType { get; set; }

    public InvalidDataIntegrityException(Type objectType) : base("Data integrity check failed. Data is corrupt or missing.")
    {
        ObjectType = objectType;
    }
}