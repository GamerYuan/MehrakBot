#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.Application.Services.Common;

public class DailyCheckInService : IDailyCheckInService
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly ILogger<DailyCheckInService> m_Logger;

    private static readonly Dictionary<GameName, string> CheckInUrls = new()
    {
        { GameName.Genshin, $"{HoYoLabDomains.GenshinApi}/event/sol/sign" },
        { GameName.HonkaiStarRail, $"{HoYoLabDomains.PublicApi}/event/luna/hkrpg/os/sign" },
        { GameName.ZenlessZoneZero, $"{HoYoLabDomains.PublicApi}/event/luna/zzz/os/sign" },
        { GameName.HonkaiImpact3, $"{HoYoLabDomains.PublicApi}/event/mani/sign" },
        { GameName.TearsOfThemis, $"{HoYoLabDomains.PublicApi}/event/luna/nxx/os/sign" }
    };

    private static readonly Dictionary<GameName, string> CheckInActIds = new()
    {
        { GameName.Genshin, "e202102251931481" },
        { GameName.HonkaiStarRail, "e202303301540311" },
        { GameName.ZenlessZoneZero, "e202406031448091" },
        { GameName.HonkaiImpact3, "e202110291205111" },
        { GameName.TearsOfThemis, "e202202281857121" }
    };

    public DailyCheckInService(IHttpClientFactory httpClientFactory,
        GameRecordApiService gameRecordApiService, ILogger<DailyCheckInService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_GameRecordApiService = gameRecordApiService;
        m_Logger = logger;
    }

    public async Task<ApiResult<(bool, string)>> CheckInAsync(ulong ltuid, string ltoken)
    {
        try
        {
            UserData? userData = await m_GameRecordApiService.GetUserDataAsync(ltuid, ltoken);
            if (userData == null)
                return ApiResult<(bool, string)>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid UID or Cookies. Please re-authenticate your profile");

            GameName[] checkInTypes = Enum.GetValues<GameName>();

            List<Task<ApiResult<bool>>> tasks = [.. checkInTypes.Select(async type =>
            {
                try
                {
                    return await CheckInHelperAsync(type, ltuid, ltoken);
                }
                catch (Exception ex)
                {
                    m_Logger.LogError(ex, "An error occurred while checking in for {Game}", type);
                    return ApiResult<bool>.Failure(HttpStatusCode.InternalServerError,
                        $"An error occurred while checking in for {type}");
                }
            })];

            await Task.WhenAll(tasks);

            StringBuilder sb = new("### Daily check-in results:\n");
            for (int i = 0; i < checkInTypes.Length; i++)
            {
                string? gameResult = tasks[i].Result.IsSuccess
                    ? tasks[i].Result.Data
                        ? "Success"
                        : "Already checked in today"
                    : tasks[i].Result.ErrorMessage;

                string gameName = checkInTypes[i].ToFriendlyString();
                sb.AppendLine($"{gameName}: {gameResult}");
            }

            return ApiResult<(bool, string)>.Success(
                (tasks.All(x => x.Result.IsSuccess || x.Result.StatusCode == HttpStatusCode.Forbidden), sb.ToString()));
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while performing daily check-in for user LtUid: {UserId}",
                ltuid);
            return ApiResult<(bool, string)>.Failure(HttpStatusCode.InternalServerError,
                "An unknown error occurred while performing daily check-in");
        }
    }

    private async Task<ApiResult<bool>> CheckInHelperAsync(GameName type, ulong ltuid, string ltoken)
    {
        if (!CheckInUrls.TryGetValue(type, out string? url) || !CheckInActIds.TryGetValue(type, out string? actId))
        {
            m_Logger.LogError("Invalid check-in type: {Type}", type);
            return ApiResult<bool>.Failure(HttpStatusCode.BadRequest, "Invalid check-in type");
        }

        HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");
        HttpRequestMessage request = new(HttpMethod.Post, url);
        CheckInApiPayload requestBody = new(actId);
        request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");

        if (type == GameName.ZenlessZoneZero) request.Headers.Add("X-Rpc-Signgame", "zzz");

        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        m_Logger.LogDebug("Sending check-in request to {Endpoint}", request.RequestUri);
        HttpResponseMessage response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Check-in request failed with status code {StatusCode}", response.StatusCode);
            return ApiResult<bool>.Failure(response.StatusCode, "An unknown error occurred during check-in");
        }

        JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (json == null)
        {
            m_Logger.LogError("Failed to parse JSON response from check-in request");
            return ApiResult<bool>.Failure(response.StatusCode, "An unknown error occurred during check-in");
        }

        int? retcode = json["retcode"]?.GetValue<int>();

        switch (retcode)
        {
            case -5003:
                m_Logger.LogInformation("User LtUid: {UserId} has already checked in today for game {Game}", ltuid,
                    type.ToString());
                return ApiResult<bool>.Success(false, -5003, response.StatusCode);

            case 0:
                m_Logger.LogInformation("User LtUid: {UserId} check-in successful for game {Game}", ltuid, type.ToString());
                return ApiResult<bool>.Success(true, 0, response.StatusCode);

            case -10002:
                m_Logger.LogInformation("User LtUid: {UserId} does not have a valid account for game {Game}", ltuid,
                    type.ToString());
                return ApiResult<bool>.Failure(HttpStatusCode.Forbidden, "No valid game account found");

            default:
                m_Logger.LogError("Check-in failed for user LtUid: {UserId} for game {Game} with retcode {Retcode}", ltuid,
                    type.ToString(), retcode);
                return ApiResult<bool>.Failure(response.StatusCode,
                    $"An unknown error occurred during check-in");
        }
    }
}
