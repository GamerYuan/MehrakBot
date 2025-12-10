namespace Mehrak.Infrastructure.Config;

public class S3StorageConfig
{
    public string Bucket { get; set; } = "";
    public string ServiceURL { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public bool ForcePathStyle { get; set; } = true;
}
