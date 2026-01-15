namespace Mehrak.Domain.Auth.Dtos;

public class LoginRequestDto
{
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
}

public class LoginResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public Guid UserId { get; init; }
    public string? Username { get; init; }
    public long DiscordUserId { get; init; }
    public string? SessionToken { get; init; }

    public bool IsSuperAdmin { get; init; }
    public bool IsRootUser { get; init; }
    public IReadOnlyCollection<string> GameWritePermissions { get; init; } = [];
    public bool RequiresPasswordReset { get; init; }
}
