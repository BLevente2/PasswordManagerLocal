namespace PasswordManagerLocalBackend.Security;

public sealed class FileKeyStore : IKeyStore
{
    private readonly string _root;

    public FileKeyStore(string rootDirectory)
    {
        _root = rootDirectory;
        Directory.CreateDirectory(_root);
    }

    private string PathFor(string id) => System.IO.Path.Combine(_root, id + ".key");

    public Task SaveAsync(string id, ReadOnlySpan<byte> protectedKey)
    {
        File.WriteAllBytes(PathFor(id), protectedKey.ToArray());
        return Task.CompletedTask;
    }

    public Task<byte[]> LoadAsync(string id)
    {
        var p = PathFor(id);
        if (!File.Exists(p)) throw new FileNotFoundException("Key not found.", p);
        return Task.FromResult(File.ReadAllBytes(p));
    }

    public Task<bool> ExistsAsync(string id)
    {
        return Task.FromResult(File.Exists(PathFor(id)));
    }
}