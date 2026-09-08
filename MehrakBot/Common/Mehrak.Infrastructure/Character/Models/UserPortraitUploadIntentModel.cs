using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Infrastructure.Character.Models;

public class UserPortraitUploadIntentModel
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

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int Attempts { get; set; }

    public DateTime? LastAttemptAtUtc { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }
}
