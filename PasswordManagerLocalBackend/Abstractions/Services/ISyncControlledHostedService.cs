using Microsoft.Extensions.Hosting;

namespace PasswordManagerLocalBackend.Abstractions.Services;

public interface ISyncControlledHostedService : IHostedService
{
    int StartOrder { get; }
}
