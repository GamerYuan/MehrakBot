#region

using System.Text.Json.Nodes;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Zzz;

internal class ZzzTowerApiService : IApiService<GameApiResponse<ZzzTowerData>, BaseHoYoApiContext>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/climbing_tower_detail";
    private const string ErrorMessage =
        "An unknown error occurred when accessing HoYoLAB API. Please try again later";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzTowerApiService> m_Logger;

    public ZzzTowerApiService(IHttpClientFactory httpClientFactory, ILogger<ZzzTowerApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<ZzzTowerData>>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<ZzzTowerData>>.Failure(StatusCode.BadParameter,
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
                    CookieFormat = "ltoken_v2={1}; ltuid_v2={0};"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);

            if (!result.IsSuccess)
                return Result<GameApiResponse<ZzzTowerData>>.Failure(result.StatusCode, result.ErrorMessage,
                    result.RequestUri);

            var json = result.Data;

            if (!HoYoLabApi.TryReadRetcode(json, out var retcode))
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, GameApiLog.Endpoint(requestUri),
                    GameApiLog.ContextId(context.UserId), GameApiLog.SafeQueryParameters(requestUri), null, null,
                    GameApiLog.OmittedMarker);
                return Result<GameApiResponse<ZzzTowerData>>.Failure(StatusCode.ExternalServerError, ErrorMessage,
                    requestUri);
            }

            if (retcode == HoYoLabRetcodes.GameRecordAuth)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, GameApiLog.ContextId(context.UserId));
                return Result<GameApiResponse<ZzzTowerData>>.Failure(StatusCode.Unauthorized,
                    HoYoLabFailureMessages.Default.Unauthorized, requestUri);
            }

            if (retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, retcode, GameApiLog.ContextId(context.UserId),
                    GameApiLog.Endpoint(requestUri), GameApiLog.SafeQueryParameters(requestUri),
                    GameApiLog.RenderBody(json));
                return Result<GameApiResponse<ZzzTowerData>>.Failure(StatusCode.ExternalServerError, ErrorMessage,
                    requestUri);
            }

            if (json["data"] is not JsonObject dataNode)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, GameApiLog.Endpoint(requestUri),
                    GameApiLog.ContextId(context.UserId), GameApiLog.SafeQueryParameters(requestUri),
                    GameApiLog.RenderBody(json));
                return Result<GameApiResponse<ZzzTowerData>>.Failure(StatusCode.ExternalServerError, ErrorMessage,
                    requestUri);
            }

            var towerNode = dataNode["climbing_tower_s3"];
            if (towerNode is null)
                return Result<GameApiResponse<ZzzTowerData>>.Success(GameApiResponse<ZzzTowerData>.Empty(),
                    requestUri: requestUri);

            if (!HoYoLabApi.TryDeserializeNode<ZzzTowerData>(towerNode, out var towerData))
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, GameApiLog.Endpoint(requestUri),
                    GameApiLog.ContextId(context.UserId), GameApiLog.SafeQueryParameters(requestUri), null, null,
                    GameApiLog.OmittedMarker);
                return Result<GameApiResponse<ZzzTowerData>>.Failure(StatusCode.ExternalServerError, ErrorMessage,
                    requestUri);
            }

            if (towerData.DisplayAvatarRankList is null)
                return HoYoLabApi.InvalidEndpointData<ZzzTowerData>(requestUri, m_Logger, context.UserId,
                    ErrorMessage);

            if (towerData.DisplayAvatarRankList.Count == 0)
            {
                m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Simulated Battle Trial",
                    GameApiLog.ContextId(context.UserId));
                return Result<GameApiResponse<ZzzTowerData>>.Success(GameApiResponse<ZzzTowerData>.Empty(),
                    requestUri: requestUri);
            }

            if (towerData.LayerInfo is null || towerData.MvpInfo is null ||
                !GameApiPayloadValidation.HasNoNullEntries(towerData.DisplayAvatarRankList) ||
                towerData.DisplayAvatarRankList.Any(avatar => string.IsNullOrEmpty(avatar.Rarity)))
                return HoYoLabApi.InvalidEndpointData<ZzzTowerData>(requestUri, m_Logger, context.UserId,
                    ErrorMessage);

            return Result<GameApiResponse<ZzzTowerData>>.Success(GameApiResponse<ZzzTowerData>.Available(towerData),
                requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<ZzzTowerData>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GameApiResponse<ZzzTowerData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving tower data", requestUri);
        }
    }
}
