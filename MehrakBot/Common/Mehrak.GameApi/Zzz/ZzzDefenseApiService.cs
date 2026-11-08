#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Zzz;

internal class ZzzDefenseApiService : IApiService<ZzzDefenseDataV2, BaseHoYoApiContext>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/hadal_info_v2";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzDefenseApiService> m_Logger;

    public ZzzDefenseApiService(IHttpClientFactory httpClientFactory, ILogger<ZzzDefenseApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<ZzzDefenseDataV2>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<ZzzDefenseDataV2>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}&schedule_type=1";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    CookieFormat = "ltoken_v2={1}; ltuid_v2={0};",
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid cookies. Please re-authenticate.",
                        Parse = "An error occurred while fetching Shiyu Defense data",
                        NonSuccessStatus = "An error occurred while fetching Shiyu Defense data"
                    }
                },
                new HoYoLabEnvelopePolicy<ZzzDefenseDataWrapper>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "An error occurred while fetching Shiyu Defense data"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                data => Task.FromResult(Result<ZzzDefenseDataV2>.Success(data.HadalInfoV2, requestUri: requestUri)));
        }
        catch (OperationCanceledException)
        {
            return Result<ZzzDefenseDataV2>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<ZzzDefenseDataV2>.Failure(StatusCode.BotError,
                "An error occurred while fetching Shiyu Defense data", requestUri);
        }
    }
}
