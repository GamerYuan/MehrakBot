namespace Mehrak.Infrastructure.Config;

public class RedisConfig
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "Mehrak_";
}
