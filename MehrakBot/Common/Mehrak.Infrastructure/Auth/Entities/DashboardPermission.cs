using System.ComponentModel.DataAnnotations;

namespace Mehrak.Infrastructure.Auth.Entities;

public class DashboardPermission
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public long DiscordId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Permission { get; set; } = string.Empty;
}
