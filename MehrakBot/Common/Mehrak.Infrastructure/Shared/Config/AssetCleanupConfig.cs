namespace Mehrak.Infrastructure.Shared.Config;

public class AssetCleanupConfig
{
    public string BackupBucket { get; set; } = "";

    public string BackupPrefix { get; set; } = "asset-cleanup";
}
