namespace Mehrak.Domain.Auth.Dtos;

public class AddDashboardUserRequestDto
{
    public string Username { get; init; } = string.Empty;
    public long DiscordUserId { get; init; }
    public bool IsSuperAdmin { get; init; }
    public IReadOnlyCollection<string> GameWritePermissions { get; init; } = Array.Empty<string>();
}

public class AddDashboardUserResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public Guid UserId { get; init; }
    public string? Username { get; init; }
    public IReadOnlyCollection<string> GameWritePermissions { get; init; } = Array.Empty<string>();
    public string? TemporaryPassword { get; init; }
    public bool RequiresPasswordReset { get; init; }
}
