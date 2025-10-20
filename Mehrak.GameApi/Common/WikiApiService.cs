using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Mehrak.GameApi.Common;

public class WikiApiService : IApiService<JsonNode, WikiApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<WikiApiService> m_Logger;

    public WikiApiService(IHttpClientFactory httpClientFactory, ILogger<WikiApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<JsonNode>> GetAsync(WikiApiContext context)
    {
        try
        {
            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get
            };
            request.Headers.Add("X-Rpc-Language", context.Game == Game.Genshin ? "zh-cn" : "en-us");
            request.RequestUri = new Uri($"{HoYoLabDomains.WikiApi}{GetEndpoint(context.Game)}?entry_page_id={context.EntryPage}");

            if (context.Game == Game.ZenlessZoneZero)
            {
                request.Headers.Add("X-Rpc-Wiki_app", "zzz");
            }

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError("Failed to retrieve page {EntryPage} for Game {Game}. Status code: {StatusCode}",
                    context.EntryPage, context.Game, response.StatusCode);
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoWiki API");
            }

            JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (json == null)
            {
                m_Logger.LogError("Failed to parse JSON response for page {EntryPage} and Game {Game}", context.EntryPage, context.Game);
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoWiki API");
            }

            return Result<JsonNode>.Success(json);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Error occurred while fetching wiki data for user {UserId}, game {Game}", context.UserId, context.Game);
            return Result<JsonNode>.Failure(StatusCode.BotError, "An error occurred while fetching wiki data.");
        }
    }

    private static string GetEndpoint(Game game)
    {
        return game switch
        {
            Game.Genshin => "/genshin/wapi/entry_page",
            Game.HonkaiStarRail => "/hsr/wapi/entry_page",
            Game.ZenlessZoneZero => "/zzz/wapi/entry_page",
            _ => throw new NotSupportedException($"Game {game} is not supported.")
        };
    }
}
