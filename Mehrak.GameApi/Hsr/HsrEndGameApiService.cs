#region

using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Hsr.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

#endregion

namespace Mehrak.GameApi.Hsr;

public abstract class HsrEndGameApiService : IApiService<HsrEndInformation, HsrEndGameApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrEndGameApiService> m_Logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly string BasePath = "/event/game_record/hkrpg/api/";

    protected HsrEndGameApiService(IHttpClientFactory httpClientFactory, ILogger<HsrEndGameApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HsrEndInformation>> GetAsync(HsrEndGameApiContext context)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<HsrEndInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var endpoint = context.GameMode switch
            {
                HsrEndGameMode.PureFiction => "challenge_story",
                HsrEndGameMode.ApocalypticShadow => "challenge_boss",
                _ => throw new NotImplementedException(),
            };
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{BasePath}{endpoint}?role_id={context.GameUid}&server={context.Region}&schedule_type=1&need_all=true");
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");
            request.Headers.Add("Ds", DSGenerator.GenerateDS());
            request.Headers.Add("X-Rpc-App_version", "1.5.0");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.Headers.Add("X-Rpc-Client_type", "5");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch {GameMode} information for gameUid: {GameUid}", context.GameMode.GetString(),
                    context.GameUid);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to fetch {GameMode} information for gameUid: {GameUid}", context.GameMode.GetString(),
                    context.GameUid);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", context.GameUid);
                return Result<HsrEndInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError(
                    "Failed to fetch {GameMode} information for gameUid: {GameUid}, retcode: {Retcode}",
                    context.GameMode.GetString(),
                    context.GameUid, json["retcode"]);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var endGameInfo = json["data"]?.Deserialize<HsrEndInformation>(JsonOptions)!;

            return Result<HsrEndInformation>.Success(endGameInfo);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get {GameMode} data for gameUid: {GameUid}, region: {Region}",
                context.GameMode.GetString(), context.GameUid, context.Region);
            return Result<HsrEndInformation>.Failure(StatusCode.BotError,
                $"An unknown error occurred while fetching {context.GameMode.GetString()} information");
        }
    }

    public async ValueTask<Dictionary<int, Stream>> GetBuffMapAsync(
        HsrEndInformation fictionData)
    {
        return await fictionData.AllFloorDetail.Where(x => !x.IsFast)
            .SelectMany(x => new List<HsrEndBuff> { x.Node1!.Buff, x.Node2!.Buff })
            .DistinctBy(x => x.Id).ToAsyncEnumerable().ToDictionaryAwaitAsync(
                async x => await Task.FromResult(x.Id),
                async x =>
                {
                    var client = m_HttpClientFactory.CreateClient("Default");
                    var response = await client.GetAsync(x.Icon);
                    return await response.Content.ReadAsStreamAsync();
                });
    }
}
