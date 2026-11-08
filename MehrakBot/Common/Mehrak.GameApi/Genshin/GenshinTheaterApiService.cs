#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinTheaterApiService : IApiService<GenshinTheaterResponseData, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinTheaterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/role_combat";

    public GenshinTheaterApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinTheaterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinTheaterResponseData>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GenshinTheaterResponseData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&need_detail=true";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var result = await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")],
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again",
                        Parse = "An error occurred while retrieving Imaginarium Theater data",
                        NonSuccessStatus = "An error occurred while retrieving Imaginarium Theater data"
                    }
                },
                new HoYoLabEnvelopePolicy<GenshinTheaterResponseData>
                {
                    UnknownRetcode = "An error occurred while retrieving Imaginarium Theater data"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);

            if (!result.IsSuccess)
                return result;

            var responseData = result.Data;
            if (!responseData.IsUnlock)
            {
                m_Logger.LogInformation(LogMessages.FeatureNotUnlocked, "Imaginarium Theater", GameApiLog.ContextId(context.UserId));
                return Result<GenshinTheaterResponseData>.Failure(StatusCode.Unauthorized,
                    "Imaginarium Theater is not unlocked yet", requestUri);
            }

            if (responseData.Data is null ||
                (responseData.Data.Count == 0 && responseData.TarotCardState is null))
            {
                m_Logger.LogError(LogMessages.DataNotFoundForFeature, "Imaginarium Theater", GameApiLog.ContextId(context.UserId));
                return Result<GenshinTheaterResponseData>.Failure(StatusCode.ExternalServerError,
                    "No Imaginarium Theater data found", requestUri);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return Result<GenshinTheaterResponseData>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GenshinTheaterResponseData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving Imaginarium Theater data", requestUri);
        }
    }
}
