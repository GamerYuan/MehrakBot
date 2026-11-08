#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

internal class HsrAnomalyApiService : IApiService<HsrAnomalyInformation, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrAnomalyApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/challenge_peak";

    public HsrAnomalyApiService(IHttpClientFactory httpClientFactory, ILogger<HsrAnomalyApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HsrAnomalyInformation>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<HsrAnomalyInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&schedule_type=1";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Headers =
                    [
                        new KeyValuePair<string, string>("DS", DSGenerator.GenerateDS()),
                        new KeyValuePair<string, string>("X-Rpc-App_version", "1.5.0"),
                        new KeyValuePair<string, string>("X-Rpc-Language", "en-us"),
                        new KeyValuePair<string, string>("X-Rpc-Client_type", "5")
                    ],
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again.",
                        Parse = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                        NonSuccessStatus = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                    }
                },
                new HoYoLabEnvelopePolicy<HsrAnomalyInformation>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);
        }
        catch (OperationCanceledException)
        {
            return Result<HsrAnomalyInformation>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<HsrAnomalyInformation>.Failure(StatusCode.BotError,
                "An error occurred while fetching Anomaly Arbitration information", requestUri);
        }
    }
}
