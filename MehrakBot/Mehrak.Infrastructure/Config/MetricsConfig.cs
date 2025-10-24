namespace Mehrak.Infrastructure.Config;

public class MetricsConfig
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 9090;
    public string Endpoint { get; set; } = "/metrics";
    public GrafanaConfig Grafana { get; set; } = new();
    public bool IncludeSystemMetrics { get; set; } = true;
    public bool CollectCommandMetrics { get; set; } = true;
}

public class GrafanaConfig
{
    public string SubPath { get; set; } = "/grafana";
    public string Domain { get; set; } = "localhost";
}
