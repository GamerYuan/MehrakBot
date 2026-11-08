#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

internal class HsrMemoryApiService : IApiService<GameApiResponse<HsrMemoryInformation>, BaseHoYoApiContext>
{
    private const string ErrorMessage =
        "An unknown error occurred when accessing HoYoLAB API. Please try again later";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrMemoryApiService> m_Logger;
    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/challenge";

    public HsrMemoryApiService(IHttpClientFactory httpClientFactory, ILogger<HsrMemoryApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<HsrMemoryInformation>>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<HsrMemoryInformation>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&schedule_type=1&need_all=true";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var request = new HoYoLabRequest
            {
                RequestUri = requestUri,
                Headers =
                [
                    new KeyValuePair<string, string>("DS", DSGenerator.GenerateDS()),
                    new KeyValuePair<string, string>("X-Rpc-App_version", "1.5.0"),
                    new KeyValuePair<string, string>("X-Rpc-Language", "en-us"),
                    new KeyValuePair<string, string>("X-Rpc-Client_type", "5")
                ]
            };

            return await HoYoLabApi.ExecuteAsync<HsrMemoryInformation, GameApiResponse<HsrMemoryInformation>>(
                request,
                new HoYoLabEnvelopePolicy<HsrMemoryInformation>
                {
                    UnknownRetcode = ErrorMessage
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                payload =>
                {
                    if (!payload.HasData || payload.BattleNum == 0)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Memory of Chaos",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<HsrMemoryInformation>>.Success(
                            GameApiResponse<HsrMemoryInformation>.Empty(), requestUri: requestUri));
                    }

                    if (payload.AllFloorDetail is null ||
                        !GameApiPayloadValidation.HasNoNullEntries(payload.AllFloorDetail) ||
                        !HasValidFloorNodes(payload.AllFloorDetail) ||
                        payload.StartTime is null || payload.EndTime is null ||
                        !HasValidCalendarTime(payload.StartTime) || !HasValidCalendarTime(payload.EndTime) ||
                        payload.Groups is null || payload.Groups.Count == 0 || payload.Groups[0] is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<HsrMemoryInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    return Task.FromResult(Result<GameApiResponse<HsrMemoryInformation>>.Success(
                        GameApiResponse<HsrMemoryInformation>.Available(payload), requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<HsrMemoryInformation>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GameApiResponse<HsrMemoryInformation>>.Failure(StatusCode.BotError,
                "An error occurred while fetching Memory of Chaos information", requestUri);
        }
    }

    private static bool HasValidFloorNodes(List<FloorDetail> floors)
    {
        return floors.All(floor => IsValidNode(floor.Node1) && IsValidNode(floor.Node2) &&
                                   IsValidNode(floor.Node3));
    }

    private static bool IsValidNode(NodeInformation? node)
    {
        return node is null ||
               (node.Avatars is not null && GameApiPayloadValidation.HasNoNullEntries(node.Avatars));
    }

    private static bool HasValidCalendarTime(ScheduleTime time)
    {
        return GameApiPayloadValidation.IsValidCalendarTime(time.Year, time.Month, time.Day, time.Hour,
            time.Minute);
    }
}
