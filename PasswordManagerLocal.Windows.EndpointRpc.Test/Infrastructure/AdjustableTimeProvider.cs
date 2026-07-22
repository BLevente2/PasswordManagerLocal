namespace PasswordManagerLocal.Windows.EndpointRpc.Test.Infrastructure;

public sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public AdjustableTimeProvider(DateTimeOffset utcNow) =>
        _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) =>
        _utcNow = _utcNow.Add(duration);
}
