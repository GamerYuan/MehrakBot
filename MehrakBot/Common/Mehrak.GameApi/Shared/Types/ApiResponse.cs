#region

using System.Text.Json.Serialization;
using Mehrak.GameApi.Shared;

#endregion

namespace Mehrak.GameApi.Shared.Types;

public class ApiResponse<T> where T : class
{
    [JsonPropertyName("data")] public T? Data { get; init; }
    [JsonPropertyName("retcode")] public int Retcode { get; init; }
    [JsonPropertyName("message")][GameApiSensitive] public required string Message { get; init; }
}
