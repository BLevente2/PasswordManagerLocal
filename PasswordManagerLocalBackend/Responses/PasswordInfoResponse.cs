using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalBackend.Responses;

public sealed class PasswordInfoResponse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;



    public static PasswordInfoResponse ConvertToPasswordInfoResponse(SecurePassword password) =>
        new PasswordInfoResponse
        {
            Id = password.Id,
            Name = password.Name,
            Description = password.Description,
            Color = password.Color,
            CreatedAt = password.CreatedAt,
            LastUpdatedAt = password.LastUpdatedAt
        };
}