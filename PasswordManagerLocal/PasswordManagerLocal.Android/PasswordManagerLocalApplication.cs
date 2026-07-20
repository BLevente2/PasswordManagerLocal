using Android.App;
using Android.Runtime;
using PasswordManagerLocal.Backend.Android;
using PasswordManagerLocal.Backend.Hosting;

namespace PasswordManagerLocal.Android;

[Application]
public sealed class PasswordManagerLocalApplication : Application
{
    public PasswordManagerLocalApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public IBackendRuntime BackendRuntime { get; private set; } = null!;
    public string ApplicationDataDirectory { get; private set; } = string.Empty;

    public override void OnCreate()
    {
        base.OnCreate();

        var composition = AndroidBackendRuntimeFactory.Create(this);
        BackendRuntime = composition.Runtime;
        ApplicationDataDirectory = composition.ApplicationDataDirectory;
    }
}
