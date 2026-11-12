namespace Mehrak.Dashboard.Asset.Models;

public sealed class CleanupByPrefixRequest
{
    public string? Prefix { get; set; }

    public bool DryRun { get; set; } = true;
}
