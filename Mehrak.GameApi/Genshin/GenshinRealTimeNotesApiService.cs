#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Genshin;

public class GenshinRealTimeNotesApiService : IApiService<GenshinRealTimeNotesData, BaseHoYoApiContext>
{
    private static readonly string ApiEndpoint = "/event/game_record/app/genshin/api/dailyNote";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinRealTimeNotesApiService> m_Logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public GenshinRealTimeNotesApiService(IHttpClientFactory httpClientFactory,
        ILogger<GenshinRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinRealTimeNotesData>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<GenshinRealTimeNotesData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}");
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch real-time notes: {StatusCode}", response.StatusCode);
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    $"Failed to fetch real-time notes: {response.ReasonPhrase}");
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<GenshinRealTimeNotesData>>(
                await response.Content.ReadAsStreamAsync(), JsonOptions);
            if (json?.Data == null)
            {
                m_Logger.LogError("Failed to parse JSON response from real-time notes API");
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.ExternalServerError,
                    "Failed to parse JSON response from real-time notes API");
            }

            if (json.Retcode == 10001)
            {
                m_Logger.LogError("Invalid ltuid or ltoken provided for real-time notes API");
                return Result<GenshinRealTimeNotesData>.Failure(StatusCode.Unauthorized,
                    "Invalid ltuid or ltoken provided for real-time notes API");
            }

            return Result<GenshinRealTimeNotesData>.Success(json.Data);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e,
                "An error occurred while fetching real-time notes for roleId {RoleId} on server {Server}",
                context.GameUid, context.Region);
            return Result<GenshinRealTimeNotesData>.Failure(StatusCode.BotError,
                "An error occurred while fetching real-time notes");
        }
    }
}
