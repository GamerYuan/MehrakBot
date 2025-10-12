#region

using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinTheaterApiService : IApiService<GenshinTheaterInformation, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinTheaterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/role_combat";

    public GenshinTheaterApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinTheaterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinTheaterInformation>> GetAsync(BaseHoYoApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&need_detail=true");
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Theater data for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse Theater data for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError("Failed to fetch Theater data for gameUid: {GameUid}, retcode: {Retcode}",
                    context.GameUid, json["retcode"]?.GetValue<int>());
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data");
            }

            // TODO: Update this logic
            if (json["data"]?["is_unlock"]?.GetValue<bool>() == false)
            {
                m_Logger.LogInformation("Imaginarium Theater is not unlocked for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "Imaginarium Theater is not unlocked yet");
            }

            var theaterInfo = json["data"]?["data"]?.Deserialize<GenshinTheaterInformation[]>();
            if (theaterInfo == null || theaterInfo.Length == 0)
            {
                m_Logger.LogError("No Theater data found for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "No Imaginarium Theater data found");
            }

            var theaterData = theaterInfo[0];
            if (!theaterData.HasDetailData || theaterData.Schedule.ScheduleType != 1)
            {
                m_Logger.LogError("No Theater data found for this cycle for gameUid: {GameUid}", context.GameUid);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "No Imaginarium Theater data found for this cycle");
            }

            return Result<GenshinTheaterInformation>.Success(theaterData);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get Theater data for gameUid: {GameUid}, region: {Region}",
                context.GameUid, context.Region);
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BotError,
                "An error occurred while retrieving Imaginarium Theater data");
        }
    }
}
