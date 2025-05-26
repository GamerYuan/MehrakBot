#region

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services;

#endregion

namespace MehrakCore.Services;

public class DailyCheckInService : IDailyCheckInService
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<DailyCheckInService> m_Logger;

    private const string GenshinCheckInApiUrl = "https://sg-hk4e-api.hoyolab.com/event/sol/sign";
    private const string HsrCheckInApiUrl = "https://sg-public-api.hoyolab.com/event/luna/hkrpg/os/sign";
    private const string ZzzCheckInApiUrl = "https://sg-public-api.hoyolab.com/event/luna/zzz/os/sign";
    private const string Hi3CheckInApiUrl = "https://sg-public-api.hoyolab.com/event/mani/sign";

    private const string GenshinCheckInActId = "e202102251931481";
    private const string HsrCheckInActId = "e202303301540311";
    private const string ZzzCheckInActId = "e202406031448091";
    private const string Hi3CheckInActId = "e202110291205111";

    public DailyCheckInService(IHttpClientFactory httpClientFactory, ILogger<DailyCheckInService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task CheckInAsync(IInteractionContext context, ulong ltuid, string ltoken)
    {
        var userId = context.Interaction.User.Id;
        m_Logger.LogInformation("User {UserId} is performing daily check-in", userId);

        List<Task<ApiResult<bool>>> tasks =
        [
            CheckInHelperAsync(CheckInType.GenshinImpact, context.Interaction.User.Id, ltuid, ltoken),
            CheckInHelperAsync(CheckInType.HonkaiStarRail, context.Interaction.User.Id, ltuid, ltoken),
            CheckInHelperAsync(CheckInType.ZenlessZoneZero, context.Interaction.User.Id, ltuid, ltoken),
            CheckInHelperAsync(CheckInType.HonkaiImpact3, context.Interaction.User.Id, ltuid, ltoken)
        ];

        await Task.WhenAll(tasks);

        var resultMessage = "### Daily check-in results:\n" +
                            $"Genshin Impact: {(tasks[0].Result.IsSuccess ? tasks[0].Result.Data ?
                                "Success" : "Already checked in today" : tasks[0].Result.ErrorMessage)}\n" +
                            $"Honkai: Star Rail: {(tasks[1].Result.IsSuccess ? tasks[1].Result.Data ?
                                "Success" : "Already checked in today" : tasks[1].Result.ErrorMessage)}\n" +
                            $"Zenless Zone Zero: {(tasks[2].Result.IsSuccess ? tasks[2].Result.Data ?
                                "Success" : "Already checked in today" : tasks[2].Result.ErrorMessage)}\n" +
                            $"Honkai Impact 3rd: {(tasks[3].Result.IsSuccess ? tasks[3].Result.Data ?
                                "Success" : "Already checked in today" : tasks[3].Result.ErrorMessage)}";

        await context.Interaction.SendFollowupMessageAsync(
            new InteractionMessageProperties().AddComponents(new TextDisplayProperties(resultMessage))
                .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral));
    }

    private async Task<ApiResult<bool>> CheckInHelperAsync(CheckInType type, ulong userId, ulong ltuid,
        string ltoken)
    {
        var url = type switch
        {
            CheckInType.GenshinImpact => GenshinCheckInApiUrl,
            CheckInType.HonkaiStarRail => HsrCheckInApiUrl,
            CheckInType.ZenlessZoneZero => ZzzCheckInApiUrl,
            CheckInType.HonkaiImpact3 => Hi3CheckInApiUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
        var actId = type switch
        {
            CheckInType.GenshinImpact => GenshinCheckInActId,
            CheckInType.HonkaiStarRail => HsrCheckInActId,
            CheckInType.ZenlessZoneZero => ZzzCheckInActId,
            CheckInType.HonkaiImpact3 => Hi3CheckInActId,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        var httpClient = m_HttpClientFactory.CreateClient("Default");
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        var requestBody = new CheckInApiPayload(actId);
        request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");

        if (type == CheckInType.ZenlessZoneZero) request.Headers.Add("X-Rpc-Signgame", "zzz");

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

    private enum CheckInType
    {
        GenshinImpact,
        HonkaiStarRail,
        ZenlessZoneZero,
        HonkaiImpact3
    }
}
