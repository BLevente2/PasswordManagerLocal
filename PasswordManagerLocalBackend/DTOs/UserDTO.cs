using PasswordManagerLocal.Backend.EncryptedDatas;
using PasswordManagerLocal.Backend.Models;

namespace PasswordManagerLocal.Backend.DTOs;

public class LoggedInUserDTO : IDisposable
{
    public Guid UId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginDate { get; set; } = DateTime.UtcNow;

    public List<EncryptedPasswordDTO> Passwords { get; set; } = new List<EncryptedPasswordDTO>();


    public LoggedInUserDTO(UserData userData)
    {
        UId = userData.UId;
        Username = userData.Username;
        FirstName = userData.FirstName;
        LastName = userData.LastName;
        Email = userData.Email;
        RegistrationDate = userData.RegistrationDate;
        LastLoginDate = userData.LastLoginDate;
        Passwords = userData.Passwords.Select(p => new EncryptedPasswordDTO(p)).ToList();
    }

    private bool _disposed;

    ~LoggedInUserDTO()
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

        if (disposing)
        {
            foreach (var password in Passwords)
            {
                password.Dispose();
            }
        }

        UId = Guid.Empty;
        Username = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
        Email = string.Empty;
        RegistrationDate = DateTime.MinValue;
        LastLoginDate = DateTime.MinValue;
        Passwords.Clear();

        _disposed = true;
    }
}