#region

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Services.Common;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Common;

public class DailyCheckInService : IDailyCheckInService
{
    private readonly UserRepository m_UserRepository;
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly GameRecordApiService m_GameRecordApiService;
    private readonly ILogger<DailyCheckInService> m_Logger;

    private static readonly Dictionary<GameName, string> CheckInUrls = new()
    {
        { GameName.Genshin, $"{HoYoLabDomains.GenshinApi}/event/sol/sign" },
        { GameName.HonkaiStarRail, $"{HoYoLabDomains.PublicApi}/event/luna/hkrpg/os/sign" },
        { GameName.ZenlessZoneZero, $"{HoYoLabDomains.PublicApi}/event/luna/zzz/os/sign" },
        { GameName.HonkaiImpact3, $"{HoYoLabDomains.PublicApi}/event/mani/sign" }
    };

    private static readonly Dictionary<GameName, string> CheckInActIds = new()
    {
        { GameName.Genshin, "e202102251931481" },
        { GameName.HonkaiStarRail, "e202303301540311" },
        { GameName.ZenlessZoneZero, "e202406031448091" },
        { GameName.HonkaiImpact3, "e202110291205111" }
    };

    public DailyCheckInService(UserRepository userRepository, IHttpClientFactory httpClientFactory,
        GameRecordApiService gameRecordApiService, ILogger<DailyCheckInService> logger)
    {
        m_UserRepository = userRepository;
        m_HttpClientFactory = httpClientFactory;
        m_GameRecordApiService = gameRecordApiService;
        m_Logger = logger;
    }

    public async Task<ApiResult<string>> CheckInAsync(ulong userId, UserModel user, uint profile, ulong ltuid,
        string ltoken)
    {
        try
        {
            m_Logger.LogInformation("User {UserId} is performing daily check-in", userId);

            var userData = await m_GameRecordApiService.GetUserDataAsync(ltuid, ltoken);
            if (userData == null)
                return ApiResult<string>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid UID or Cookies. Please re-authenticate your profile");

            var checkInTypes = Enum.GetValues<GameName>();

            var tasks = checkInTypes.Select(async type =>
            {
                try
                {
                    return await CheckInHelperAsync(type, userId, ltuid, ltoken);
                }
                catch (Exception ex)
                {
                    m_Logger.LogError(ex, "An error occurred while checking in for {Game}", type);
                    return ApiResult<bool>.Failure(HttpStatusCode.InternalServerError,
                        $"An error occurred while checking in for {type}");
                }
            }).ToList();

            await Task.WhenAll(tasks);

            var sb = new StringBuilder("### Daily check-in results:\n");
            for (int i = 0; i < checkInTypes.Length; i++)
            {
                var gameResult = tasks[i].Result.IsSuccess
                    ? tasks[i].Result.Data
                        ? "Success"
                        : "Already checked in today"
                    : tasks[i].Result.ErrorMessage;

                var gameName = GetFormattedGameName(checkInTypes[i]);
                sb.AppendLine($"{gameName}: {gameResult}");
            }

            if (tasks.All(x => x.Result.IsSuccess || x.Result.StatusCode == HttpStatusCode.Forbidden))
            {
                user.Profiles!.First(x => x.ProfileId == profile).LastCheckIn = DateTime.UtcNow;
                await m_UserRepository.CreateOrUpdateUserAsync(user);
            }

            return ApiResult<string>.Success(sb.ToString());
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while performing daily check-in for user {UserId}",
                userId);
            return ApiResult<string>.Failure(HttpStatusCode.InternalServerError,
                "An unknown error occurred while performing daily check-in");
        }
    }


    private static string GetFormattedGameName(GameName type)
    {
        return type switch
        {
            GameName.Genshin => "Genshin Impact",
            GameName.HonkaiStarRail => "Honkai: Star Rail",
            GameName.ZenlessZoneZero => "Zenless Zone Zero",
            GameName.HonkaiImpact3 => "Honkai Impact 3rd",
            _ => type.ToString()
        };
    }

    private async Task<ApiResult<bool>> CheckInHelperAsync(GameName type, ulong userId, ulong ltuid,
        string ltoken)
    {
        if (!CheckInUrls.TryGetValue(type, out var url) || !CheckInActIds.TryGetValue(type, out var actId))
        {
            m_Logger.LogError("Invalid check-in type: {Type}", type);
            return ApiResult<bool>.Failure(HttpStatusCode.BadRequest, "Invalid check-in type");
        }

        var httpClient = m_HttpClientFactory.CreateClient("Default");
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        var requestBody = new CheckInApiPayload(actId);
        request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");

        if (type == GameName.ZenlessZoneZero) request.Headers.Add("X-Rpc-Signgame", "zzz");

        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        m_Logger.LogDebug("Sending check-in request to {Endpoint}", request.RequestUri);
        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Check-in request failed with status code {StatusCode}", response.StatusCode);
            return ApiResult<bool>.Failure(response.StatusCode, "An unknown error occurred during check-in");
        }

        var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (json == null)
        {
            m_Logger.LogError("Failed to parse JSON response from check-in request");
            return ApiResult<bool>.Failure(response.StatusCode, "An unknown error occurred during check-in");
        }

        var retcode = json["retcode"]?.GetValue<int>();

        switch (retcode)
        {
            case -5003:
                m_Logger.LogInformation("User {UserId} has already checked in today for game {Game}", userId,
                    type.ToString());
                return ApiResult<bool>.Success(false, -5003, response.StatusCode);
            case 0:
                m_Logger.LogInformation("User {UserId} check-in successful for game {Game}", userId, type.ToString());
                return ApiResult<bool>.Success(true, 0, response.StatusCode);
            case -10002:
                m_Logger.LogInformation("User {UserId} does not have a valid account for game {Game}", userId,
                    type.ToString());
                return ApiResult<bool>.Failure(HttpStatusCode.Forbidden, "No valid game account found");
            default:
                m_Logger.LogError("Check-in failed for user {UserId} for game {Game} with retcode {Retcode}", userId,
                    type.ToString(), retcode);
                return ApiResult<bool>.Failure(response.StatusCode,
                    $"An unknown error occurred during check-in");
        }
    }
}
