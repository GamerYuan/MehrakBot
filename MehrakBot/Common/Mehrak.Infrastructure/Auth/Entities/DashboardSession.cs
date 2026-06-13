using System.ComponentModel.DataAnnotations;

namespace Mehrak.Infrastructure.Auth.Entities;

public class DashboardSession
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [Required]
    [MaxLength(32)]
    public string Token { get; set; } = string.Empty;

    public long DiscordId { get; set; }

    public string? AccessToken { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? LastTokenValidation { get; set; }

    [MaxLength(45)]
    public string? LoginIp { get; set; }

    [MaxLength(512)]
    public string? UserAgent { get; set; }

    [MaxLength(256)]
    public string? Location { get; set; }
}
