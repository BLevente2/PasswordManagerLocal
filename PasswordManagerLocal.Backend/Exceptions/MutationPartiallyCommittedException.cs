namespace PasswordManagerLocal.Backend.Exceptions;

public class MutationPartiallyCommittedException : Exception
{
    public MutationPartiallyCommittedException(
        string message,
        bool requiresProcessRestart = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RequiresProcessRestart = requiresProcessRestart;
    }

    public bool RequiresProcessRestart { get; }
}
