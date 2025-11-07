#region

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Common;

public class DailyCheckInApiService : IApiService<CheckInStatus, CheckInApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<DailyCheckInApiService> m_Logger;

    private static readonly Dictionary<Game, string> CheckInUrls = new()
    {
        { Game.Genshin, $"{HoYoLabDomains.GenshinApi}/event/sol/sign" },
        { Game.HonkaiStarRail, $"{HoYoLabDomains.PublicApi}/event/luna/hkrpg/os/sign" },
        { Game.ZenlessZoneZero, $"{HoYoLabDomains.PublicApi}/event/luna/zzz/os/sign" },
        { Game.HonkaiImpact3, $"{HoYoLabDomains.PublicApi}/event/mani/sign" },
        { Game.TearsOfThemis, $"{HoYoLabDomains.PublicApi}/event/luna/nxx/os/sign" }
    };

    private static readonly Dictionary<Game, string> CheckInActIds = new()
    {
        { Game.Genshin, "e202102251931481" },
        { Game.HonkaiStarRail, "e202303301540311" },
        { Game.ZenlessZoneZero, "e202406031448091" },
        { Game.HonkaiImpact3, "e202110291205111" },
        { Game.TearsOfThemis, "e202202281857121" }
    };

    public DailyCheckInApiService(IHttpClientFactory httpClientFactory, ILogger<DailyCheckInApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<CheckInStatus>> GetAsync(CheckInApiContext context)
    {
        if (!CheckInUrls.TryGetValue(context.Game, out string? requestUri) ||
            !CheckInActIds.TryGetValue(context.Game, out string? actId))
        {
            m_Logger.LogError("Invalid check-in type: {Type}", context.Game);
            return Result<CheckInStatus>.Failure(StatusCode.BadParameter, "Invalid check-in type");
        }

        try
        {
            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Post, requestUri);
            CheckInApiPayload requestBody = new() { ActId = actId };
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");

            if (context.Game == Game.ZenlessZoneZero) request.Headers.Add("X-Rpc-Signgame", "zzz");

            request.Content =
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Info-level outbound request (no headers)
            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            HttpResponseMessage response = await httpClient.SendAsync(request);

            // Info-level inbound response (status only)
            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<CheckInStatus>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred during check-in", requestUri);
            }

            JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());

            if (json == null)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.LtUid.ToString());
                return Result<CheckInStatus>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred during check-in", requestUri);
            }

            int? retcode = json["retcode"]?.GetValue<int>();

            // Info-level API retcode after parse
            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, retcode ?? -1,
                context.UserId);

            switch (retcode)
            {
                case -5003:
                    m_Logger.LogInformation(LogMessages.AlreadyCheckedIn, context.LtUid, context.Game.ToString());
                    return Result<CheckInStatus>.Success(CheckInStatus.AlreadyCheckedIn, requestUri: requestUri);

                case 0:
                    m_Logger.LogInformation("User LtUid: {LtUid} check-in successful for game {Game}", context.LtUid,
                        context.Game.ToString());
                    return Result<CheckInStatus>.Success(CheckInStatus.Success, requestUri: requestUri);

                case -10002:
                    m_Logger.LogInformation(LogMessages.NoValidProfile, context.LtUid, context.Game.ToString());
                    return Result<CheckInStatus>.Success(CheckInStatus.NoValidProfile, requestUri: requestUri);

                default:
                    m_Logger.LogError("Check-in failed for user LtUid: {LtUid} for game {Game} with retcode {Retcode}",
                        context.LtUid,
                        context.Game.ToString(), retcode);
                    return Result<CheckInStatus>.Failure(StatusCode.ExternalServerError,
                        $"An unknown error occurred during check-in", requestUri);
            }
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                CheckInUrls.GetValueOrDefault(context.Game, "unknown"), context.LtUid.ToString());
            return Result<CheckInStatus>.Failure(StatusCode.BotError,
                "An error occurred during check-in");
        }
    }

    private sealed class CheckInApiPayload
    {
        [JsonPropertyName("act_id")] public string ActId { get; set; } = string.Empty;
    }
}
