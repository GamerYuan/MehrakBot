namespace Mehrak.Domain.Services.Abstractions;

public interface IDashboardMetrics : IMetricsService
{
    void TrackUserLogin(string userId);

    void TrackUserLogout(string userId);
}
