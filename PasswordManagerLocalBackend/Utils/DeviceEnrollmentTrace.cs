using PasswordManagerLocalBackend.Constants;
using System.Diagnostics;
using System.Text;

namespace PasswordManagerLocalBackend.Utils;

public static class DeviceEnrollmentTrace
{
#if DEBUG
    private const long MaxLogBytes = 256 * 1024;
    private static readonly object Lock = new();
#endif

    public static string LogPath
    {
        get
        {
            try
            {
                return Path.Combine(PathConstants.AppRootFolder, "device-enrollment.log");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "PasswordManagerLocal-device-enrollment.log");
            }
        }
    }

    public static void InitializeForCurrentBuild()
    {
#if !DEBUG
        TryDeleteReleaseLog(LogPath);
        TryDeleteReleaseLog($"{LogPath}.old");
        TryDeleteReleaseLog(Path.Combine(Path.GetTempPath(), "PasswordManagerLocal-device-enrollment.log"));
        TryDeleteReleaseLog(Path.Combine(Path.GetTempPath(), "PasswordManagerLocal-device-enrollment.log.old"));
#endif
    }

    [Conditional("DEBUG")]
    public static void Info(string message) => Write("INFO", message, null);

    [Conditional("DEBUG")]
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
#if DEBUG
        try
        {
            lock (Lock)
            {
                var path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RotateIfNeeded(path);

                var builder = new StringBuilder();
                builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
                builder.Append(" [");
                builder.Append(level);
                builder.Append("] ");
                builder.AppendLine(message);

                if (exception is not null)
                    builder.AppendLine(exception.ToString());

                File.AppendAllText(path, builder.ToString());
            }
        }
        catch
        {
        }
#endif
    }

#if DEBUG
    private static void RotateIfNeeded(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= MaxLogBytes)
                return;

            var oldPath = path + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);

            File.Move(path, oldPath);
        }
        catch
        {
        }
    }
#else
    private static void TryDeleteReleaseLog(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
#endif
}
