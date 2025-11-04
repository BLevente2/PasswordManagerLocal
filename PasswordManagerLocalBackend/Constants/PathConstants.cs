    using System.Security;

    namespace PasswordManagerLocalBackend.Constants;

    public static class PathConstants
    {
        public const string AppFolderName = "PasswordManagerLocal";
        public static readonly string AppRootFolder;
    public const string KeyFileName = "dbkey.bin";
    public const string DbFileName = "app.db";

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
            basePath = "";
            root = "";
            try
            {
                var candidate = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData,
                    Environment.SpecialFolderOption.Create);

                if (string.IsNullOrWhiteSpace(candidate)) return false;

                var created = EnsureDirectory(Path.Combine(candidate, AppFolderName));
                basePath = candidate;
                root = created;
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
            {
                lastError = ex;
                return false;
            }
        }

        private static bool TryInitFromBaseDirectory(out string basePath, out string root, ref Exception? lastError)
        {
            basePath = "";
            root = "";
            try
            {
                var candidate = AppContext.BaseDirectory;
                var created = EnsureDirectory(Path.Combine(candidate, AppFolderName));
                basePath = candidate;
                root = created;
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
            {
                lastError = ex;
                return false;
            }
        }

        private static bool TryInitFromTemp(out string basePath, out string root, ref Exception? lastError)
        {
            basePath = "";
            root = "";
            try
            {
                var candidate = Path.GetTempPath();
                var created = EnsureDirectory(Path.Combine(candidate, AppFolderName));
                basePath = candidate;
                root = created;
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
            {
                lastError = ex;
                return false;
            }
        }

        private static string EnsureDirectory(string path)
        {
            return Directory.CreateDirectory(path).FullName;
        }
    }