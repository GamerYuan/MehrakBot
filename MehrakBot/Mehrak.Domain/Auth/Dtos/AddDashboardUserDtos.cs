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
    public bool IsRootUser { get; init; }
}

public class ChangeDashboardPasswordRequestDto
{
    public Guid UserId { get; init; }
    public string CurrentPassword { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

public class ChangeDashboardPasswordResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public bool SessionsInvalidated { get; init; }
}

public class ForceResetDashboardPasswordRequestDto
{
    public Guid UserId { get; init; }
    public string NewPassword { get; init; } = string.Empty;
}

public class UpdateDashboardUserRequestDto
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public long DiscordUserId { get; init; }
    public bool IsSuperAdmin { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyCollection<string> GameWritePermissions { get; init; } = Array.Empty<string>();
}

public class UpdateDashboardUserResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public Guid UserId { get; init; }
    public string? Username { get; init; }
    public bool IsActive { get; init; }
    public bool IsSuperAdmin { get; init; }
    public IReadOnlyCollection<string> GameWritePermissions { get; init; } = Array.Empty<string>();
    public bool IsRootUser { get; init; }
}

public class RemoveDashboardUserResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
}

public class DashboardUserSummaryDto
{
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;

    // Serialized as string to avoid precision loss in JavaScript clients (Discord IDs exceed Number.MAX_SAFE_INTEGER)
    public string DiscordUserId { get; init; } = string.Empty;
    public bool IsSuperAdmin { get; init; }
    public bool IsRootUser { get; init; }
    public IReadOnlyCollection<string> GameWritePermissions { get; init; } = [];
}

public class DashboardUserRequireResetResultDto
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public bool SessionsInvalidated { get; init; }
}
