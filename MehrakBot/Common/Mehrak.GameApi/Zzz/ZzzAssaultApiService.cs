#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Zzz;

public class ZzzAssaultApiService : IApiService<ZzzAssaultData, BaseHoYoApiContext>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/hadal_mem_detail_v2";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzAssaultApiService> m_Logger;

    public ZzzAssaultApiService(IHttpClientFactory clientFactory, ILogger<ZzzAssaultApiService> logger)
    {
        m_HttpClientFactory = clientFactory;
        m_Logger = logger;
    }

    public async Task<Result<ZzzAssaultData>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<ZzzAssaultData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?region={context.Region}&uid={context.GameUid}&schedule_type=1";

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
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again",
                        Parse = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                        NonSuccessStatus = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                    }
                },
                new HoYoLabEnvelopePolicy<ZzzAssaultData>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);
        }
        catch (OperationCanceledException)
        {
            return Result<ZzzAssaultData>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<ZzzAssaultData>.Failure(StatusCode.BotError,
                "An error occurred while retrieving assault data", requestUri);
        }
    }
}
