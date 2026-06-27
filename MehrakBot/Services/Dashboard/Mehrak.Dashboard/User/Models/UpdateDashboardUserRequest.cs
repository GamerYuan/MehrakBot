namespace Mehrak.Dashboard.User.Models;

public class UpdateDashboardUserRequest
{
    public bool IsSuperAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    public IEnumerable<string> GameWritePermissions { get; set; } = [];
}
