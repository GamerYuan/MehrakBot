using System.Diagnostics;
using System.Globalization;
using Mehrak.Domain.Shared.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Mehrak.ServiceDefaults;

/// <summary>
/// Application-only tracing policy for dependency calls whose negative response is a normal domain answer. The span,
/// its duration and <c>http.response.status_code</c> are kept; only the error classification of an expected 404 is
/// cleared and a bounded expected-miss tag is added. All other statuses and exceptions keep the default
/// instrumentation classification.
/// </summary>
public static class ExpectedDependencyTracing
{
    private const string HttpResponseStatusCodeTag = "http.response.status_code";
    private const string LegacyHttpStatusCodeTag = "http.status_code";
    internal const string ErrorTypeTag = "error.type";
    private const int NotFoundStatusCode = 404;

    /// <summary>
    /// Adds the expected-miss processor to a tracer provider. Call this only from the host whose traces should use
    /// the policy; other hosts keep the default HTTP instrumentation behavior.
    /// </summary>
    public static TracerProviderBuilder AddExpectedDependencyNormalization(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddProcessor(new ExpectedDependencyProcessor());
    }

    internal static bool IsExpectedMiss(string kind, int statusCode)
    {
        return statusCode == NotFoundStatusCode &&
               kind is ExpectedDependency.ExistenceProbeKind or ExpectedDependency.WikiContentKind;
    }

    internal static bool TryGetStatusCode(Activity activity, out int statusCode)
    {
        return TryReadStatusCode(activity.GetTagItem(HttpResponseStatusCodeTag), out statusCode) ||
               TryReadStatusCode(activity.GetTagItem(LegacyHttpStatusCodeTag), out statusCode);
    }

    private static bool TryReadStatusCode(object? value, out int statusCode)
    {
        switch (value)
        {
            case int number:
                statusCode = number;
                return true;
            case long number when number is >= int.MinValue and <= int.MaxValue:
                statusCode = (int)number;
                return true;
            case string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var parsed):
                statusCode = parsed;
                return true;
            default:
                statusCode = 0;
                return false;
        }
    }
}

internal sealed class ExpectedDependencyProcessor : BaseProcessor<Activity>
{
    private const string HttpClientSourceName = "System.Net.Http";

    public override void OnStart(Activity activity)
    {
        if (activity.Kind != ActivityKind.Client) return;
        if (!string.Equals(activity.Source.Name, HttpClientSourceName, StringComparison.Ordinal)) return;

        var kind = ExpectedDependency.KindValue(ExpectedDependency.Current);
        if (kind is null) return;

        activity.SetTag(ExpectedDependency.KindTag, kind);
    }

    public override void OnEnd(Activity activity)
    {
        if (activity.Kind != ActivityKind.Client) return;
        if (!string.Equals(activity.Source.Name, HttpClientSourceName, StringComparison.Ordinal)) return;
        if (activity.GetTagItem(ExpectedDependency.KindTag) is not string kind) return;
        if (activity.Status != ActivityStatusCode.Error) return;
        if (!ExpectedDependencyTracing.TryGetStatusCode(activity, out var statusCode)) return;
        if (!ExpectedDependencyTracing.IsExpectedMiss(kind, statusCode)) return;
        if (!IsStatusDerivedError(activity, statusCode)) return;

        activity.SetStatus(ActivityStatusCode.Unset);
        activity.SetTag(ExpectedDependencyTracing.ErrorTypeTag, null);
        activity.SetTag(ExpectedDependency.OutcomeTag, ExpectedDependency.ExpectedMissOutcome);
    }

    private static bool IsStatusDerivedError(Activity activity, int statusCode)
    {
        return activity.GetTagItem(ExpectedDependencyTracing.ErrorTypeTag) switch
        {
            null => true,
            int value => value == statusCode,
            long value => value == statusCode,
            string text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var parsed) && parsed == statusCode,
            _ => false
        };
    }
}
