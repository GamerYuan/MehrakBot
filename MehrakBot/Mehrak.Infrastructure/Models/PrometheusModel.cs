#region

#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.Infrastructure.Models;

#endregion

public class PrometheusResponse
{
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("data")] public required PrometheusData Data { get; init; }
}

public class PrometheusData
{
    [JsonPropertyName("resultType")] public required string ResultType { get; init; }
    [JsonPropertyName("result")] public required List<PrometheusResult> Result { get; init; }
}

public class PrometheusResult
{
    [JsonPropertyName("metric")] public required Dictionary<string, string> Metric { get; init; }
    [JsonPropertyName("value")] public required object[] Value { get; init; }
}
