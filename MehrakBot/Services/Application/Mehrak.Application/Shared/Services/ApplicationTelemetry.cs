using System.Diagnostics;

namespace Mehrak.Application.Shared.Services;

public static class ApplicationTelemetry
{
    public const string ActivitySourceName = "Mehrak.Application";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
