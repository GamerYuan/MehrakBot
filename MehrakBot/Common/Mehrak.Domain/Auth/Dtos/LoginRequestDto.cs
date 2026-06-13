using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Auth.Dtos;

public class LoginResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string DiscordUserId { get; init; } = string.Empty;
    public string? SessionToken { get; init; }

    public bool IsSuperAdmin { get; init; }
    public bool IsRootUser { get; init; }
    public IReadOnlyCollection<Game> GameWritePermissions { get; init; } = [];
}
