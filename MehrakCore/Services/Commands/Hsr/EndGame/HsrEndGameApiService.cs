#region

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MehrakCore.ApiResponseTypes.Hsr;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Hsr.EndGame.PureFiction;

internal class HsrEndGameApiService : IApiService<BaseHsrEndGameCommandExecutor>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrEndGameApiService> m_Logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly string BasePath = "/event/game_record/hkrpg/api/";

    public HsrEndGameApiService(IHttpClientFactory httpClientFactory, ILogger<HsrEndGameApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<Result<HsrEndInformation>> GetEndGameDataAsync(string gameUid, string region,
        ulong ltuid, string ltoken, EndGameMode gameMode)
    {
        try
        {
            var endpoint = gameMode switch
            {
                EndGameMode.PureFiction => "challenge_story",
                EndGameMode.ApocalypticShadow => "challenge_boss",
                _ => throw new ArgumentOutOfRangeException(nameof(gameMode), gameMode, null)
            };
            var client = m_HttpClientFactory.CreateClient("Default");
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{BasePath}{endpoint}?role_id={gameUid}&server={region}&schedule_type=1&need_all=true");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            request.Headers.Add("Ds", DSGenerator.GenerateDS());
            request.Headers.Add("X-Rpc-App_version", "1.5.0");
            request.Headers.Add("X-Rpc-Language", "en-us");
            request.Headers.Add("X-Rpc-Client_type", "5");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch {GameMode} information for gameUid: {GameUid}", gameMode.GetString(),
                    gameUid);
                return Result<HsrEndInformation>.Failure(response.StatusCode,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to fetch {GameMode} information for gameUid: {GameUid}", gameMode.GetString(),
                    gameUid);
                return Result<HsrEndInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return Result<HsrEndInformation>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again.");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError(
                    "Failed to fetch {GameMode} information for gameUid: {GameUid}, retcode: {Retcode}",
                    gameMode.GetString(),
                    gameUid, json["retcode"]);
                return Result<HsrEndInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later");
            }

            var pureFictionInfo = json["data"]?.Deserialize<HsrEndInformation>(JsonOptions)!;

            return Result<HsrEndInformation>.Success(pureFictionInfo);
        }
        catch
        {
            m_Logger.LogError("Failed to get {GameMode} data for gameUid: {GameUid}, region: {Region}",
                gameMode.GetString(),
                gameUid, region);
            return Result<HsrEndInformation>.Failure(HttpStatusCode.InternalServerError,
                $"An unknown error occurred while fetching {gameMode.GetString()} information");
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
