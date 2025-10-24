namespace Mehrak.Infrastructure.Models;

public struct SystemResource
{
    public double CpuUsage { get; set; }
    public long MemoryUsed { get; set; }
    public long MemoryTotal { get; set; }
}
