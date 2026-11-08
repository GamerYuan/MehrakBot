#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Zzz;

internal class ZzzDefenseApiService : IApiService<GameApiResponse<ZzzDefenseDataV2>, BaseHoYoApiContext>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/hadal_info_v2";
    private const string ErrorMessage = "An error occurred while fetching Shiyu Defense data";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzDefenseApiService> m_Logger;

    public ZzzDefenseApiService(IHttpClientFactory httpClientFactory, ILogger<ZzzDefenseApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<ZzzDefenseDataV2>>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<ZzzDefenseDataV2>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}&schedule_type=1";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var request = new HoYoLabRequest
            {
                RequestUri = requestUri,
                CookieFormat = "ltoken_v2={1}; ltuid_v2={0};"
            };

            return await HoYoLabApi.ExecuteAsync<ZzzDefenseDataWrapper, GameApiResponse<ZzzDefenseDataV2>>(
                request,
                new HoYoLabEnvelopePolicy<ZzzDefenseDataWrapper>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = ErrorMessage
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                wrapper =>
                {
                    var payload = wrapper.HadalInfoV2;
                    if (payload is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<ZzzDefenseDataV2>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    if (payload.Brief is null || payload.FifthLayerDetail?.LayerChallengeInfoList is null ||
                        payload.FifthLayerDetail.LayerChallengeInfoList.Count == 0)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Shiyu Defense",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<ZzzDefenseDataV2>>.Success(
                            GameApiResponse<ZzzDefenseDataV2>.Empty(), requestUri: requestUri));
                    }

                    if (!GameApiPayloadValidation.IsUnixSeconds(payload.BeginTime) ||
                        !GameApiPayloadValidation.IsUnixSeconds(payload.EndTime) ||
                        string.IsNullOrEmpty(payload.Brief.Rating) ||
                        !GameApiPayloadValidation.HasNoNullEntries(payload.FifthLayerDetail.LayerChallengeInfoList) ||
                        payload.FifthLayerDetail.LayerChallengeInfoList.Any(info =>
                            string.IsNullOrEmpty(info.Rating) || info.AvatarList is null ||
                            !GameApiPayloadValidation.HasNoNullEntries(info.AvatarList) ||
                            info.AvatarList.Any(avatar => string.IsNullOrEmpty(avatar.Rarity))))
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<ZzzDefenseDataV2>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    return Task.FromResult(Result<GameApiResponse<ZzzDefenseDataV2>>.Success(
                        GameApiResponse<ZzzDefenseDataV2>.Available(payload), requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<ZzzDefenseDataV2>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GameApiResponse<ZzzDefenseDataV2>>.Failure(StatusCode.BotError, ErrorMessage,
                requestUri);
        }
    }
}
