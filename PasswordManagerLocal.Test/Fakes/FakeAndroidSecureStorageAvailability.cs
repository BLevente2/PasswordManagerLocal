using PasswordManagerLocal.Android.Runtime;

namespace PasswordManagerLocal.Test.Fakes;

public sealed class FakeAndroidSecureStorageAvailability : IAndroidSecureStorageAvailability
{
    public bool IsAvailable { get; set; } = true;
}
