using System.Text.Json.Serialization;

namespace Mehrak.GameApi.Common.Types;

public class ApiResponse<T> where T : class
{
    [JsonPropertyName("data")] public T? Data { get; init; }
    [JsonPropertyName("retcode")] public int Retcode { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }
}
