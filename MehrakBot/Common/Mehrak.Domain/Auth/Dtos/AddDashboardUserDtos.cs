using Mehrak.Domain.Shared.Enums;

namespace Mehrak.Domain.Auth.Dtos;

public class AddDashboardUserRequestDto
{
    public long DiscordUserId { get; init; }
    public IReadOnlyCollection<Game> GameWritePermissions { get; init; } = [];
}

public class AddDashboardUserResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public long DiscordUserId { get; init; }
    public IReadOnlyCollection<Game> GameWritePermissions { get; init; } = [];
    public bool IsRootUser { get; init; }
}

public class UpdateDashboardUserRequestDto
{
    public long DiscordUserId { get; init; }
    public bool IsSuperAdmin { get; init; }
    public IReadOnlyCollection<Game> GameWritePermissions { get; init; } = [];
}

public class UpdateDashboardUserResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public long DiscordUserId { get; init; }
    public bool IsSuperAdmin { get; init; }
    public IReadOnlyCollection<Game> GameWritePermissions { get; init; } = [];
    public bool IsRootUser { get; init; }
}

public class RemoveDashboardUserResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
}

public class DashboardUserSummaryDto
{
    // Serialized as string to avoid precision loss in JavaScript clients (Discord IDs exceed Number.MAX_SAFE_INTEGER)
    public string DiscordUserId { get; init; } = string.Empty;
    public bool IsSuperAdmin { get; init; }
    public bool IsRootUser { get; init; }
    public IReadOnlyCollection<Game> GameWritePermissions { get; init; } = [];
}
