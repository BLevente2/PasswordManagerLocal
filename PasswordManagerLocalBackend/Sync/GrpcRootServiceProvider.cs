namespace PasswordManagerLocalBackend.Sync;

public sealed class GrpcRootServiceProvider
{
    public GrpcRootServiceProvider(IServiceProvider services)
    {
        Services = services;
    }




    public IServiceProvider Services { get; }
}
