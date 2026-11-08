#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinAbyssApiService : IApiService<GenshinAbyssInformation, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinAbyssApiService> m_Logger;
    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/spiralAbyss";

    public GenshinAbyssApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinAbyssApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinAbyssInformation>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GenshinAbyssInformation>.Failure(StatusCode.BadParameter,
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
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again.",
                        Parse = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                        NonSuccessStatus = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                    }
                },
                new HoYoLabEnvelopePolicy<GenshinAbyssInformation>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);
        }
        catch (OperationCanceledException)
        {
            return Result<GenshinAbyssInformation>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GenshinAbyssInformation>.Failure(StatusCode.BotError,
                "An error occurred while fetching Abyss information", requestUri);
        }
    }
}
