using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Character.Models;

public class UserPortraitUploadDto
{
    public Guid Id { get; init; }
    public long DiscordUserId { get; init; }
    public Game Game { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public string SHA256Hash { get; init; } = string.Empty;
    public string S3Key { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public UserPortraitConfigDto Config { get; init; } = default!;
    public DateTime CreatedAt { get; init; }
}

public class UserPortraitConfigDto
{
    public int? OffsetX { get; init; }
    public int? OffsetY { get; init; }
    public float? TargetScale { get; init; }
    public bool? FlipX { get; init; }
}

public class UploadPortraitResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public Guid? UploadId { get; init; }
    public UserPortraitUploadDto? Portrait { get; init; }
}
