namespace Mehrak.Dashboard.Asset.Models;

public sealed class CleanupBySuffixRequest
{
    public string? Suffix { get; set; }

    public bool DryRun { get; set; } = true;
}
