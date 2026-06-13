using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Character.Models;

[Index(nameof(DiscordUserId), nameof(Game), nameof(CharacterName))]
[Index(nameof(DiscordUserId), nameof(Game), nameof(CharacterName), nameof(IsActive))]
public class UserPortraitUpload
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public long DiscordUserId { get; set; }

    public Game Game { get; set; }

    [Required]
    [MaxLength(100)]
    public string CharacterName { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string SHA256Hash { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string S3Key { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public UserPortraitConfigModel Config { get; set; } = default!;
}
