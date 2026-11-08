#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

public class HsrRealTimeNotesApiService : IApiService<HsrRealTimeNotesData, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrRealTimeNotesApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/note";

    public HsrRealTimeNotesApiService(IHttpClientFactory httpClientFactory, ILogger<HsrRealTimeNotesApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HsrRealTimeNotesData>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<HsrRealTimeNotesData>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Headers =
                    [
                        new KeyValuePair<string, string>("X-Rpc-Client_type", "5"),
                        new KeyValuePair<string, string>("X-Rpc-App_version", "1.5.0"),
                        new KeyValuePair<string, string>("X-Rpc-Language", "en-us"),
                        new KeyValuePair<string, string>("DS", DSGenerator.GenerateDS())
                    ]
                },
                new HoYoLabEnvelopePolicy<HsrRealTimeNotesData>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "Failed to parse JSON response from real-time notes API"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token);
        }
        catch (OperationCanceledException)
        {
            return Result<HsrRealTimeNotesData>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<HsrRealTimeNotesData>.Failure(StatusCode.BotError,
                "An error occurred while fetching real-time notes", requestUri);
        }
    }
}
