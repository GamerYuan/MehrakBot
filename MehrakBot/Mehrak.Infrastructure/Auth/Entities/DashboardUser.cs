namespace Mehrak.Infrastructure.Auth.Entities;

public class DashboardUser
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public long DiscordId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSuperAdmin { get; set; } = false;
    public bool RequirePasswordReset { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<DashboardSession> Sessions { get; set; } = [];
    public ICollection<DashboardGamePermission> GamePermissions { get; set; } = [];
    public bool IsRootUser { get; set; } = false;
}

public class DashboardSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public DashboardUser User { get; set; } = default!;
    public string SessionToken { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }

    public bool IsExpired() => DateTime.UtcNow >= ExpiresAtUtc;
}

public class DashboardGamePermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DashboardUser User { get; set; } = default!;

    public string GameCode { get; set; } = default!;

    public bool AllowWrite { get; set; } = false;
}
