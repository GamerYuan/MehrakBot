#region

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Constants;
using MehrakCore.Models;
using Microsoft.Extensions.Logging;

#endregion

namespace MehrakCore.Services.Commands.Genshin.Stygian;

internal class GenshinStygianApiService : IApiService<GenshinStygianCommandExecutor>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinStygianApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/hard_challenge";

    public GenshinStygianApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinStygianApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async ValueTask<Result<GenshinStygianInformation>> GetStygianDataAsync(string gameUid, string region,
        ulong ltuid, string ltoken)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={gameUid}&server={region}&need_detail=true");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Stygian data for gameUid: {GameUid}", gameUid);
                return Result<GenshinStygianInformation>.Failure(response.StatusCode,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse Stygian data for gameUid: {GameUid}", gameUid);
                return Result<GenshinStygianInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return Result<GenshinStygianInformation>.Failure(HttpStatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError("Failed to fetch Stygian data for gameUid: {GameUid}, retcode: {Retcode}",
                    gameUid, json["retcode"]?.GetValue<int>());
                return Result<GenshinStygianInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            var stygianData = json["data"]?.Deserialize<GenshinStygianInformation>();
            if (stygianData == null)
            {
                m_Logger.LogError("Failed to deserialize Stygian data for gameUid: {GameUid}", gameUid);
                return Result<GenshinStygianInformation>.Failure(HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            return Result<GenshinStygianInformation>.Success(stygianData);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while fetching Stygian data for gameUid: {GameUid}", gameUid);
            return Result<GenshinStygianInformation>.Failure(HttpStatusCode.InternalServerError,
                "An error occurred while retrieving Stygian Onslaught data");
        }
    }

    public async ValueTask<Stream> GetMonsterImageAsync(string imageUrl)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            var response = await client.GetAsync(imageUrl);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch monster image from {ImageUrl}", imageUrl);
                throw new CommandException("An error occurred while retrieving monster image");
            }

            return await response.Content.ReadAsStreamAsync();
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while fetching monster image from {ImageUrl}", imageUrl);
            throw new CommandException("An error occurred while retrieving monster image", e);
        }
    }
}
