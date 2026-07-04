using System.Security;

namespace PasswordManagerLocal.Backend.Constants;

public static class PathConstants
{
    public const string AppFolderName = "PasswordManagerLocal";
    public const string DbConfigFileName = "DbConfig.bin";
    public const string AppConfigFileName = "config.json";
    public const string LegacyDbKeyFileName = "dbkey.bin";
    public const string DbFileName = "app.db";

    public static readonly string AppRootFolder;

    static PathConstants()
    {
        Exception? lastError = null;

        if (TryInitFromLocalAppData(out var _, out var root, ref lastError) ||
            TryInitFromBaseDirectory(out var _, out root, ref lastError) ||
            TryInitFromTemp(out var _, out root, ref lastError))
        {
            AppRootFolder = root;
            return;
        }

        throw new InvalidOperationException("Could not determine or create application root folder.", lastError);
    }

    private static bool TryInitFromLocalAppData(out string basePath, out string root, ref Exception? lastError)
    {
        basePath = string.Empty;
        root = string.Empty;
        try
        {
            var candidate = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create);

            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            var created = EnsureDirectory(Path.Combine(candidate, AppFolderName));
            basePath = candidate;
            root = created;
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            lastError = exception;
            return false;
        }
    }

    private static bool TryInitFromBaseDirectory(out string basePath, out string root, ref Exception? lastError)
    {
        basePath = string.Empty;
        root = string.Empty;
        try
        {
            var candidate = AppContext.BaseDirectory;
            var created = EnsureDirectory(Path.Combine(candidate, AppFolderName));
            basePath = candidate;
            root = created;
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            lastError = exception;
            return false;
        }
    }

    private static bool TryInitFromTemp(out string basePath, out string root, ref Exception? lastError)
    {
        basePath = string.Empty;
        root = string.Empty;
        try
        {
            var candidate = Path.GetTempPath();
            var created = EnsureDirectory(Path.Combine(candidate, AppFolderName));
            basePath = candidate;
            root = created;
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
