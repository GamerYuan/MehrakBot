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

public class HsrEndGameApiService : IApiService<GameApiResponse<HsrEndInformation>, HsrEndGameApiContext>
{
    private const string ErrorMessage =
        "An unknown error occurred when accessing HoYoLAB API. Please try again later";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrEndGameApiService> m_Logger;

    private static readonly string BasePath = "/event/game_record/hkrpg/api/";

    public HsrEndGameApiService(IHttpClientFactory httpClientFactory, ILogger<HsrEndGameApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<HsrEndInformation>>> GetAsync(HsrEndGameApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<HsrEndInformation>>.Failure(StatusCode.BadParameter,
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

            return await HoYoLabApi.ExecuteAsync<HsrEndInformation, GameApiResponse<HsrEndInformation>>(
                request,
                new HoYoLabEnvelopePolicy<HsrEndInformation>
                {
                    UnknownRetcode = ErrorMessage
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                payload =>
                {
                    if (!payload.HasData)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, context.GameMode.GetString(),
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<HsrEndInformation>>.Success(
                            GameApiResponse<HsrEndInformation>.Empty(), requestUri: requestUri));
                    }

                    if (payload.AllFloorDetail is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<HsrEndInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    if (payload.AllFloorDetail.Count == 0 ||
                        payload.AllFloorDetail.All(x => x is { Node1: null, Node2: null, Node3: null }))
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, context.GameMode.GetString(),
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<HsrEndInformation>>.Success(
                            GameApiResponse<HsrEndInformation>.Empty(), requestUri: requestUri));
                    }

                    if (!GameApiPayloadValidation.HasNoNullEntries(payload.AllFloorDetail) ||
                        !HasValidFloorNodes(payload.AllFloorDetail) ||
                        payload.Groups is null || payload.Groups.Count == 0 ||
                        payload.Groups[0] is null || payload.Groups[0].BeginTime is null ||
                        payload.Groups[0].EndTime is null ||
                        !HasValidCalendarTime(payload.Groups[0].BeginTime) ||
                        !HasValidCalendarTime(payload.Groups[0].EndTime))
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<HsrEndInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    return Task.FromResult(Result<GameApiResponse<HsrEndInformation>>.Success(
                        GameApiResponse<HsrEndInformation>.Available(payload), requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<HsrEndInformation>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{BasePath}{context.GameMode.GetString()}",
                context.UserId, e);
            return Result<GameApiResponse<HsrEndInformation>>.Failure(StatusCode.BotError,
                $"An unknown error occurred while fetching {context.GameMode.GetString()} information", requestUri);
        }
    }

    private static bool HasValidFloorNodes(List<HsrEndFloorDetail> floors)
    {
        return floors.All(floor => IsValidNode(floor.Node1) && IsValidNode(floor.Node2) &&
                                   IsValidNode(floor.Node3));
    }

    private static bool IsValidNode(HsrEndNodeInformation? node)
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
