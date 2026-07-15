namespace Mehrak.Infrastructure.Shared.Config;

public class AttachmentStorageConfig
{
    public string Bucket { get; set; } = "";

    public int TtlMinutes { get; set; } = 60;

    public int ExpirationScanIntervalMinutes { get; set; } = 15;
}
