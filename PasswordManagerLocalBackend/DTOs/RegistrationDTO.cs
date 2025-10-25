namespace PasswordManagerLocal.Backend.DTOs
{
    public class RegistrationDTO : IDisposable
    {
        public string Username { get; set; } = string.Empty;
        public byte[] Password { get; set; } = [];
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = false;

        private bool _disposed;

        ~RegistrationDTO()
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
            if (_disposed)
                return;

                Array.Clear(Password, 0, Password.Length);
                Password = Array.Empty<byte>();

            Username = string.Empty;
            FirstName = string.Empty;
            LastName = string.Empty;
            Email = string.Empty;

            _disposed = true;
        }
    }
}