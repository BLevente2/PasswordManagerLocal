namespace PasswordManagerLocal.Services;

public interface IClipboardWriter
{
    Task<bool> TrySetTextAsync(string text);
}
