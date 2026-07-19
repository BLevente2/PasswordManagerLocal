using PasswordManagerLocal.Backend.Constants;
using System.Security;

namespace PasswordManagerLocal.Backend.Hosting;

public static class ApplicationPaths
{
    public static readonly string AppRootFolder;

    static ApplicationPaths()
    {
        Exception? lastError = null;

        if (TryInitFromLocalAppData(out var root, ref lastError) ||
            TryInitFromBaseDirectory(out root, ref lastError) ||
            TryInitFromTemp(out root, ref lastError))
        {
            AppRootFolder = root;
            return;
        }

        throw new InvalidOperationException("Could not determine or create application root folder.", lastError);
    }

    private static bool TryInitFromLocalAppData(out string root, ref Exception? lastError)
    {
        root = string.Empty;
        try
        {
            var candidate = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create);

            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            root = EnsureDirectory(Path.Combine(candidate, ApplicationFileNames.AppFolderName));
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            lastError = exception;
            return false;
        }
    }

    private static bool TryInitFromBaseDirectory(out string root, ref Exception? lastError)
    {
        root = string.Empty;
        try
        {
            root = EnsureDirectory(Path.Combine(AppContext.BaseDirectory, ApplicationFileNames.AppFolderName));
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            lastError = exception;
            return false;
        }
    }

    private static bool TryInitFromTemp(out string root, ref Exception? lastError)
    {
        root = string.Empty;
        try
        {
            root = EnsureDirectory(Path.Combine(Path.GetTempPath(), ApplicationFileNames.AppFolderName));
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            lastError = exception;
            return false;
        }
    }

    private static string EnsureDirectory(string path) =>
        Directory.CreateDirectory(path).FullName;
}
