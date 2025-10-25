namespace PasswordManagerLocal.Backend.DTOs
{
    public class LoginDTO : IDisposable
    {
        public string Username { get; set; } = string.Empty;
        public byte[] Password { get; set; } = [];
        public bool RememberMe { get; set; } = false;

        private bool _disposed;

        ~LoginDTO()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

                Array.Clear(Password, 0, Password.Length);
                Password = Array.Empty<byte>();

            _disposed = true;
        }
    }
}