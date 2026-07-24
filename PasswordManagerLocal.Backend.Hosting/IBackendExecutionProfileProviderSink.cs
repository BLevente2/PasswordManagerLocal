using PasswordManagerLocal.Backend.Abstractions.Services;

namespace PasswordManagerLocal.Backend.Hosting;

internal interface IBackendExecutionProfileProviderSink
{
    void SetExecutionProfileProvider(IBackendExecutionProfileProvider executionProfileProvider);
}
