#region

using System.Text.Json.Nodes;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

#endregion

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
            var endpoint = GetEndpoint(context.Game);
            var requestUri = $"{HoYoLabDomains.WikiApi}{endpoint}?entry_page_id={context.EntryPage}";

            m_Logger.LogInformation(LogMessages.ReceivedRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("X-Rpc-Language", context.Game == Game.Genshin ? "zh-cn" : "en-us");

            if (context.Game == Game.ZenlessZoneZero)
                request.Headers.Add("X-Rpc-Wiki_app", "zzz");
            else if (context.Game == Game.HonkaiStarRail) request.Headers.Add("X-Rpc-Wiki_app", "hsr");

            m_Logger.LogDebug(LogMessages.SendingRequest, requestUri);
            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoWiki API", requestUri);
            }

            JsonNode? json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync());

            if (json == null)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.EntryPage);
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoWiki API", requestUri);
            }

            var retcode = json["retcode"]?.GetValue<int>() ?? -1;

            if (retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, retcode, context.EntryPage, requestUri);
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoWiki API", requestUri);
            }

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.EntryPage);
            return Result<JsonNode>.Success(json, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.WikiApi}{GetEndpoint(context.Game)}", context.EntryPage);
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
