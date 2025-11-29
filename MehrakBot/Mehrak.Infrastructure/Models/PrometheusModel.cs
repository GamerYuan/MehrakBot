#region

#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.Infrastructure.Models;

#endregion

public class PrometheusResponse
{
    [JsonPropertyName("status")] public string Status { get; init; }
    [JsonPropertyName("data")] public PrometheusData Data { get; init; }
}

public class PrometheusData
{
    [JsonPropertyName("resultType")] public string ResultType { get; init; }
    [JsonPropertyName("result")] public List<PrometheusResult> Result { get; init; }
}

public class PrometheusResult
{
    [JsonPropertyName("metric")] public Dictionary<string, string> Metric { get; init; }
    [JsonPropertyName("value")] public object[] Value { get; init; }
}
