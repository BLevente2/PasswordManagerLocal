namespace PasswordManagerLocal.Backend.Exceptions;

public sealed class SyncDeltaDeferredException : InvalidOperationException
{
    public Guid ModelId { get; }

    public SyncDeltaDeferredException(Guid modelId)
        : base($"Synchronization delta for model '{modelId}' could not be applied yet because the required profile encryption key is unavailable.")
    {
        ModelId = modelId;
    }
}
