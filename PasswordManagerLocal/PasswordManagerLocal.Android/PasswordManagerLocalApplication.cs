using Android.App;
using Android.Runtime;
using PasswordManagerLocal.Backend.Abstractions;
using PasswordManagerLocal.Backend.Android;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Android;

[Application]
public sealed class PasswordManagerLocalApplication : Application
{
    public PasswordManagerLocalApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public IBackendRuntime BackendRuntime { get; private set; } = null!;
    public IBackendRuntimeLifetimeCoordinator LifetimeCoordinator { get; private set; } = null!;
    public IBackgroundSyncSettingsStore BackgroundSyncSettingsStore { get; private set; } = null!;
    public string ApplicationDataDirectory { get; private set; } = string.Empty;

    public override void OnCreate()
    {
        base.OnCreate();

        var composition = AndroidBackendRuntimeFactory.Create(this);
        BackendRuntime = composition.Runtime;
        LifetimeCoordinator = composition.LifetimeCoordinator;
        BackgroundSyncSettingsStore = new FileBackgroundSyncSettingsStore(
            composition.ApplicationDataDirectory);
        ApplicationDataDirectory = composition.ApplicationDataDirectory;
    }

    public IFrontendBackendClient<IEndpoints> CreateBackendClient() =>
        new InProcessFrontendBackendClient(BackendRuntime, LifetimeCoordinator);

    public override void OnTerminate()
    {
        try
        {
            if (BackendRuntime is not null)
            {
                BackendRuntime
                    .DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }
        finally
        {
            base.OnTerminate();
        }
    }
}
