#region

using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

public class HsrEndGameApiService : IApiService<HsrEndInformation, HsrEndGameApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrEndGameApiService> m_Logger;

    private static readonly string BasePath = "/event/game_record/hkrpg/api/";

    public HsrEndGameApiService(IHttpClientFactory httpClientFactory, ILogger<HsrEndGameApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HsrEndInformation>> GetAsync(HsrEndGameApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<HsrEndInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        string? requestUri = null;

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var endpoint = context.GameMode switch
            {
                HsrEndGameMode.PureFiction => "challenge_story",
                HsrEndGameMode.ApocalypticShadow => "challenge_boss",
                _ => throw new NotImplementedException()
            };

            requestUri =
                $"{HoYoLabDomains.PublicApi}{BasePath}{endpoint}?role_id={context.GameUid}&server={context.Region}&schedule_type=1&need_all=true";

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
                new HoYoLabEnvelopePolicy<HsrEndInformation>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);
        }
        catch (OperationCanceledException)
        {
            return Result<HsrEndInformation>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{BasePath}{context.GameMode.GetString()}", context.UserId, e);
            return Result<HsrEndInformation>.Failure(StatusCode.BotError,
                $"An unknown error occurred while fetching {context.GameMode.GetString()} information", requestUri);
        }
    }
}
