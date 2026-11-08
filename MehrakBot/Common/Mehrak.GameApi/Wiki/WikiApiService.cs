#region

using System.Text.Json.Nodes;
using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Wiki;

public class WikiApiService : IApiService<JsonNode, WikiApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<WikiApiService> m_Logger;

    public WikiApiService(IHttpClientFactory httpClientFactory, ILogger<WikiApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<JsonNode>> GetAsync(WikiApiContext context, CancellationToken cancellationToken = default)
    {
        string? requestUri = null;

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var endpoint = GetEndpoint(context.Game);
            requestUri = $"{HoYoLabDomains.WikiApi}{endpoint}?entry_page_id={context.EntryPage}";

            var headers = new List<KeyValuePair<string, string>>
            {
                new("X-Rpc-Language", context.Locale.ToLocaleString())
            };

            if (context.Game == Game.ZenlessZoneZero)
                headers.Add(new KeyValuePair<string, string>("X-Rpc-Wiki_app", "zzz"));
            else if (context.Game == Game.HonkaiStarRail)
                headers.Add(new KeyValuePair<string, string>("X-Rpc-Wiki_app", "hsr"));

            var result = await HoYoLabApi.SendJsonAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    RequiresCredentials = false,
                    Headers = headers
                },
                new HoYoLabCredentials(context.UserId, 0, string.Empty),
                m_HttpClientFactory, m_Logger, scope.Token);

            if (!result.IsSuccess)
                return Result<JsonNode>.Failure(result.StatusCode, result.ErrorMessage, result.RequestUri);

            var json = result.Data;
            if (!HoYoLabApi.TryReadRetcode(json, out var retcode))
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, GameApiLog.Endpoint(requestUri),
                    GameApiLog.ContextId(context.UserId), GameApiLog.SafeQueryParameters(requestUri), null, null, GameApiLog.OmittedMarker);
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoWiki API", requestUri);
            }

            if (retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, retcode, GameApiLog.ContextId(context.UserId),
                    GameApiLog.Endpoint(requestUri), GameApiLog.SafeQueryParameters(requestUri),
                    GameApiLog.RenderBody(json));
                return Result<JsonNode>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoWiki API", requestUri);
            }

            return Result<JsonNode>.Success(json, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<JsonNode>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, requestUri ?? HoYoLabDomains.WikiApi, context.UserId, e);
            return Result<JsonNode>.Failure(StatusCode.BotError, "An error occurred while fetching wiki data.",
                requestUri);
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

public class WikiApiContext : IApiContext
{
    public ulong UserId { get; }
    public Game Game { get; }
    public string EntryPage { get; }
    public WikiLocales Locale { get; }

    public WikiApiContext(ulong userId, Game game, string entryPage, WikiLocales locale = WikiLocales.EN)
    {
        UserId = userId;
        Game = game;
        EntryPage = entryPage;
        Locale = locale;
    }
}
