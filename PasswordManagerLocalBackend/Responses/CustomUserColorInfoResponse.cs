using PasswordManagerLocalBackend.Models.Encrypted;

namespace PasswordManagerLocalBackend.Responses;

public sealed class CustomUserColorInfoResponse
{
    public Guid Id { get; set; }
    public string? ColorName { get; set; } = null;
    public string ColorCode { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    public static CustomUserColorInfoResponse ConvertToCustomUserColorInfoResponse(CustomUserColor color) =>
        new CustomUserColorInfoResponse
        {
            Id = color.Id,
            ColorName = color.ColorName,
            ColorCode = color.ColorCode,
            LastUpdatedAt = color.LastUpdatedAt
        };
}
