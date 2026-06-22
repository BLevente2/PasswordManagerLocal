using PasswordManagerLocalBackend.Abstractions.Services;

namespace PasswordManagerLocalTest.Fakes;

public sealed class FakeControlledHostedService : ISyncControlledHostedService
{
    private readonly string _name;
    private readonly IList<string> _calls;

    public FakeControlledHostedService(string name, int startOrder, IList<string> calls)
    {
        _name = name;
        StartOrder = startOrder;
        _calls = calls;
    }

    public int StartOrder { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _calls.Add($"start:{_name}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _calls.Add($"stop:{_name}");
        return Task.CompletedTask;
    }
}
