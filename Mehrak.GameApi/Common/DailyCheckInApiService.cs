#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
        if (!CheckInUrls.TryGetValue(context.Game, out string? url) || !CheckInActIds.TryGetValue(context.Game, out string? actId))
        {
            m_Logger.LogError("Invalid check-in type: {Type}", context.Game);
            return Result<CheckInStatus>.Failure(StatusCode.BadParameter, "Invalid check-in type");
        }

        HttpClient httpClient = m_HttpClientFactory.CreateClient("Default");
        HttpRequestMessage request = new(HttpMethod.Post, url);
        CheckInApiPayload requestBody = new() { ActId = actId };
        request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");

        if (context.Game == Game.ZenlessZoneZero) request.Headers.Add("X-Rpc-Signgame", "zzz");

        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        m_Logger.LogDebug("Sending check-in request to {Endpoint}", request.RequestUri);
        HttpResponseMessage response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            m_Logger.LogError("Check-in request failed with status code {StatusCode}", response.StatusCode);
            return Result<CheckInStatus>.Failure(StatusCode.ExternalServerError, "An unknown error occurred during check-in");
        }

        JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (json == null)
        {
            m_Logger.LogError("Failed to parse JSON response from check-in request");
            return Result<CheckInStatus>.Failure(StatusCode.ExternalServerError, "An unknown error occurred during check-in");
        }

        int? retcode = json["retcode"]?.GetValue<int>();

        switch (retcode)
        {
            case -5003:
                m_Logger.LogInformation("User LtUid: {UserId} has already checked in today for game {Game}", context.LtUid,
                    context.Game.ToString());
                return Result<CheckInStatus>.Success(CheckInStatus.AlreadyCheckedIn);

            case 0:
                m_Logger.LogInformation("User LtUid: {UserId} check-in successful for game {Game}", context.LtUid,
                    context.Game.ToString());
                return Result<CheckInStatus>.Success(CheckInStatus.Success);

            case -10002:
                m_Logger.LogInformation("User LtUid: {UserId} does not have a valid account for game {Game}", context.LtUid,
                    context.Game.ToString());
                return Result<CheckInStatus>.Success(CheckInStatus.NoValidProfile);

            default:
                m_Logger.LogError("Check-in failed for user LtUid: {UserId} for game {Game} with retcode {Retcode}", context.LtUid,
                    context.Game.ToString(), retcode);
                return Result<CheckInStatus>.Failure(StatusCode.ExternalServerError,
                    $"An unknown error occurred during check-in");
        }
    }

    private sealed class CheckInApiPayload
    {
        [JsonPropertyName("act_id")]
        public string ActId { get; set; } = string.Empty;
    }
}
