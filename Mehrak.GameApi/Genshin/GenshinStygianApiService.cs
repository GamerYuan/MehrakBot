#region

using Mehrak.Domain.Common;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinStygianApiService : IApiService<GenshinStygianInformation>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinStygianApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/hard_challenge";

    public GenshinStygianApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinStygianApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinStygianInformation>> GetAsync(ulong ltuid, string ltoken,
        string gameUid = "", string region = "")
    {
        if (string.IsNullOrEmpty(gameUid) || string.IsNullOrEmpty(region))
        {
            m_Logger.LogError("Game UID or region is null or empty");
            return Result<GenshinStygianInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={gameUid}&server={region}&need_detail=true");
            request.Headers.Add("Cookie", $"ltuid_v2={ltuid}; ltoken_v2={ltoken}");
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to fetch Stygian data for gameUid: {GameUid}", gameUid);
                return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse Stygian data for gameUid: {GameUid}", gameUid);
                return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            if (json["retcode"]?.GetValue<int>() == 10001)
            {
                m_Logger.LogError("Invalid cookies for gameUid: {GameUid}", gameUid);
                return Result<GenshinStygianInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again");
            }

            if (json["retcode"]?.GetValue<int>() != 0)
            {
                m_Logger.LogError("Failed to fetch Stygian data for gameUid: {GameUid}, retcode: {Retcode}",
                    gameUid, json["retcode"]?.GetValue<int>());
                return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            var stygianData = json["data"]?.Deserialize<GenshinStygianInformation>();
            if (stygianData == null)
            {
                m_Logger.LogError("Failed to deserialize Stygian data for gameUid: {GameUid}", gameUid);
                return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Stygian Onslaught data");
            }

            return Result<GenshinStygianInformation>.Success(stygianData);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "An error occurred while fetching Stygian data for gameUid: {GameUid}", gameUid);
            return Result<GenshinStygianInformation>.Failure(StatusCode.ExternalServerError,
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
