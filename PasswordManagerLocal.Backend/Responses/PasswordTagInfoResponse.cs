using PasswordManagerLocal.Backend.Models.Encrypted;
using PasswordManagerLocal.Backend.Utils;

namespace PasswordManagerLocal.Backend.Responses;

public sealed class PasswordTagInfoResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    public static PasswordTagInfoResponse ConvertToPasswordTagInfoResponse(PasswordTag tag) =>
        new PasswordTagInfoResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Color = tag.Color,
            LastUpdatedAt = UtcDateTimeUtil.ToUtc(tag.LastUpdatedAt)
        };
}
