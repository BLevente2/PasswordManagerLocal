using Microsoft.VisualStudio.TestTools.UnitTesting;
using PasswordManagerLocal.Backend.Hosting;
using PasswordManagerLocal.Runtime.Abstractions;

namespace PasswordManagerLocal.Test.Backend.Hosting;

[TestClass]
public sealed class FileBackgroundSyncSettingsStoreTests
{
    [TestMethod]
    public async Task MissingFile_ReturnsDisabled()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileBackgroundSyncSettingsStore(directory);
            var settings = await store.ReadAsync();

            Assert.IsFalse(settings.IsEnabled);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [TestMethod]
    public async Task WriteReadAndReplace_RoundTripsWithoutTemporaryFiles()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileBackgroundSyncSettingsStore(directory);

            await store.WriteAsync(new BackgroundSyncSettings(true));
            Assert.IsTrue((await store.ReadAsync()).IsEnabled);

            await store.WriteAsync(new BackgroundSyncSettings(false));
            Assert.IsFalse((await store.ReadAsync()).IsEnabled);
            Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly).Length);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ReadAndWrite_RespectCancellation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new FileBackgroundSyncSettingsStore(directory);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await store.ReadAsync(cancellation.Token));
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await store.WriteAsync(
                    new BackgroundSyncSettings(true),
                    cancellation.Token));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"PasswordManagerLocal.BackgroundSyncSettings.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }
}
