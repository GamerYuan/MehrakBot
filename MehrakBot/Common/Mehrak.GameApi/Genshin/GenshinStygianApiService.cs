#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinStygianApiService : IApiService<GenshinStygianInformation, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinStygianApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/hard_challenge";

    public GenshinStygianApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinStygianApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinStygianInformation>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GenshinStygianInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&need_detail=true";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again",
                        Parse = "An error occurred while retrieving Stygian Onslaught data",
                        NonSuccessStatus = "An error occurred while retrieving Stygian Onslaught data"
                    }
                },
                new HoYoLabEnvelopePolicy<GenshinStygianInformation>
                {
                    UnknownRetcode = "An error occurred while retrieving Stygian Onslaught data"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);
        }
        catch (OperationCanceledException)
        {
            return Result<GenshinStygianInformation>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GenshinStygianInformation>.Failure(StatusCode.BotError,
                "An error occurred while retrieving Stygian Onslaught data", requestUri);
        }
    }
}
