using System.ComponentModel.DataAnnotations;

namespace Mehrak.Infrastructure.Character.Models;

public class UserPortraitDeletionModel
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid? UserPortraitUploadId { get; set; }

    [Required]
    [MaxLength(512)]
    public string S3Key { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptAtUtc { get; set; }

    [MaxLength(1000)]
    public string? LastError { get; set; }
}
