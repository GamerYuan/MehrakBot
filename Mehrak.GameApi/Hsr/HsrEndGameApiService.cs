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

public abstract class HsrEndGameApiService : IApiService<HsrEndInformation>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrEndGameApiService> m_Logger;

    protected abstract HsrEndGameMode GameMode { get; }

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

    public async Task<Result<HsrEndInformation>> GetAsync(ulong ltuid, string ltoken,
        string gameUid = "", string region = "")
    {
        if (string.IsNullOrEmpty(gameUid) || string.IsNullOrEmpty(region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<HsrEndInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var endpoint = GameMode switch
            {
                HsrEndGameMode.PureFiction => "challenge_story",
                HsrEndGameMode.ApocalypticShadow => "challenge_boss",
                _ => throw new NotImplementedException(),
            };
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{BasePath}{endpoint}?role_id={gameUid}&server={region}&schedule_type=1&need_all=true");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            request.Headers.Add("Ds", DSGenerator.GenerateDS());
            request.Headers.Add("X-Rpc-App_version", "1.5.0");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.Headers.Add("X-Rpc-Client_type", "5");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch {GameMode} information for gameUid: {GameUid}", GameMode.GetString(),
                    gameUid);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to fetch {GameMode} information for gameUid: {GameUid}", GameMode.GetString(),
                    gameUid);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return Result<HsrEndInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError(
                    "Failed to fetch {GameMode} information for gameUid: {GameUid}, retcode: {Retcode}",
                    GameMode.GetString(),
                    gameUid, json["retcode"]);
                return Result<HsrEndInformation>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var endGameInfo = json["data"]?.Deserialize<HsrEndInformation>(JsonOptions)!;

            return Result<HsrEndInformation>.Success(endGameInfo);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to get {GameMode} data for gameUid: {GameUid}, region: {Region}",
                GameMode.GetString(), gameUid, region);
            return Result<HsrEndInformation>.Failure(StatusCode.BotError,
                $"An unknown error occurred while fetching {GameMode.GetString()} information");
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

    public class HsrPureFictionApiService : HsrEndGameApiService
    {
        protected override HsrEndGameMode GameMode => HsrEndGameMode.PureFiction;

        public HsrPureFictionApiService(IHttpClientFactory httpClientFactory,
            ILogger<HsrPureFictionApiService> logger)
            : base(httpClientFactory, logger) { }
    }

    public class HsrApocalypticShadowApiService : HsrEndGameApiService
    {
        protected override HsrEndGameMode GameMode => HsrEndGameMode.ApocalypticShadow;

        public HsrApocalypticShadowApiService(IHttpClientFactory httpClientFactory,
            ILogger<HsrApocalypticShadowApiService> logger)
            : base(httpClientFactory, logger) { }
    }
}
