#region

using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Common.Types;

public class ApiResponse<T> where T : class
{
    [JsonPropertyName("data")] public T? Data { get; init; }
    [JsonPropertyName("retcode")] public int Retcode { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; }
}
