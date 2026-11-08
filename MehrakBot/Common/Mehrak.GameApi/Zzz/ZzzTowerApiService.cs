using System.Text.Json.Nodes;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.GameApi.Zzz;

internal class ZzzTowerApiService : IApiService<ZzzTowerData, BaseHoYoApiContext>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/climbing_tower_detail";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzTowerApiService> m_Logger;

    public ZzzTowerApiService(IHttpClientFactory httpClientFactory, ILogger<ZzzTowerApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<ZzzTowerData>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<ZzzTowerData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?region={context.Region}&uid={context.GameUid}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var result = await HoYoLabApi.SendJsonAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    CookieFormat = "ltoken_v2={1}; ltuid_v2={0};",
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again",
                        Parse = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                        NonSuccessStatus = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                    }
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);

            if (!result.IsSuccess)
                return Result<ZzzTowerData>.Failure(result.StatusCode, result.ErrorMessage, result.RequestUri);

            var json = result.Data;

            if (!HoYoLabApi.TryReadRetcode(json, out var retcode))
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, GameApiLog.Endpoint(requestUri),
                    GameApiLog.ContextId(context.UserId), GameApiLog.SafeQueryParameters(requestUri), null, null, GameApiLog.OmittedMarker);
                return Result<ZzzTowerData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            if (retcode == HoYoLabRetcodes.GameRecordAuth)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, GameApiLog.ContextId(context.UserId));
                return Result<ZzzTowerData>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again", requestUri);
            }

            if (retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, retcode, GameApiLog.ContextId(context.UserId),
                    GameApiLog.Endpoint(requestUri), GameApiLog.SafeQueryParameters(requestUri),
                    GameApiLog.RenderBody(json));
                return Result<ZzzTowerData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            if (json["data"] is not JsonObject dataNode)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, GameApiLog.Endpoint(requestUri), GameApiLog.ContextId(context.UserId),
                    GameApiLog.SafeQueryParameters(requestUri), GameApiLog.RenderBody(json));
                return Result<ZzzTowerData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            var towerNode = dataNode["climbing_tower_s3"];
            if (towerNode is null)
                return Result<ZzzTowerData>.Success(GetEmptyData(), requestUri: requestUri);

            if (!HoYoLabApi.TryDeserializeNode<ZzzTowerData>(towerNode, out var towerData))
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, GameApiLog.Endpoint(requestUri),
                    GameApiLog.ContextId(context.UserId), GameApiLog.SafeQueryParameters(requestUri), null, null, GameApiLog.OmittedMarker);
                return Result<ZzzTowerData>.Failure(StatusCode.ExternalServerError,
                    "An unknown error occurred when accessing HoYoLAB API. Please try again later", requestUri);
            }

            return Result<ZzzTowerData>.Success(towerData, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<ZzzTowerData>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<ZzzTowerData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving tower data", requestUri);
        }
    }

    private static ZzzTowerData GetEmptyData()
    {
        return new() { DisplayAvatarRankList = [], LayerInfo = new() { MedalIcon = string.Empty }, MvpInfo = new() };
    }
}
