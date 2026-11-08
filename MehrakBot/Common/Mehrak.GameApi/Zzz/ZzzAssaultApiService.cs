#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Zzz;

public class ZzzAssaultApiService : IApiService<GameApiResponse<ZzzAssaultData>, BaseHoYoApiContext>
{
    private const string ApiEndpoint = "/event/game_record_zzz/api/zzz/hadal_mem_detail_v2";
    private const string ErrorMessage =
        "An unknown error occurred when accessing HoYoLAB API. Please try again later";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzAssaultApiService> m_Logger;

    public ZzzAssaultApiService(IHttpClientFactory clientFactory, ILogger<ZzzAssaultApiService> logger)
    {
        m_HttpClientFactory = clientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<ZzzAssaultData>>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<ZzzAssaultData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?region={context.Region}&uid={context.GameUid}&schedule_type=1";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var request = new HoYoLabRequest
            {
                RequestUri = requestUri,
                CookieFormat = "ltoken_v2={1}; ltuid_v2={0};"
            };

            return await HoYoLabApi.ExecuteAsync<ZzzAssaultData, GameApiResponse<ZzzAssaultData>>(
                request,
                new HoYoLabEnvelopePolicy<ZzzAssaultData>
                {
                    UnknownRetcode = ErrorMessage
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                payload =>
                {
                    if (!payload.HasData)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Deadly Assault",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<ZzzAssaultData>>.Success(
                            GameApiResponse<ZzzAssaultData>.Empty(), requestUri: requestUri));
                    }

                    if (payload.List is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<ZzzAssaultData>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    if (payload.List.Count == 0)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Deadly Assault",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<ZzzAssaultData>>.Success(
                            GameApiResponse<ZzzAssaultData>.Empty(), requestUri: requestUri));
                    }

                    if (payload.StartTime is null || payload.EndTime is null ||
                        !HasValidFloors(payload.List) ||
                        (payload.HasHard && !HasValidFloors(payload.HardList)) ||
                        !HasValidCalendarTime(payload.StartTime) ||
                        !HasValidCalendarTime(payload.EndTime))
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<ZzzAssaultData>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    return Task.FromResult(Result<GameApiResponse<ZzzAssaultData>>.Success(
                        GameApiResponse<ZzzAssaultData>.Available(payload), requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<ZzzAssaultData>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GameApiResponse<ZzzAssaultData>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving assault data", requestUri);
        }
    }

    private static bool HasValidFloors(List<AssaultFloorDetail>? floors)
    {
        return floors is not null && GameApiPayloadValidation.HasNoNullEntries(floors) &&
               floors.All(floor => floor.AvatarList is not null && floor.Boss is not null &&
                   floor.Buff is not null && floor.Boss.Count > 0 && floor.Buff.Count > 0 &&
                   GameApiPayloadValidation.HasNoNullEntries(floor.AvatarList) &&
                   floor.AvatarList.All(avatar => !string.IsNullOrEmpty(avatar.Rarity)) &&
                   GameApiPayloadValidation.HasNoNullEntries(floor.Boss) &&
                   floor.Boss.All(boss => boss.Name is not null) &&
                   GameApiPayloadValidation.HasNoNullEntries(floor.Buff) &&
                   floor.Buff.All(buff => buff.Name is not null));
    }

    private static bool HasValidCalendarTime(ScheduleTime time)
    {
        return GameApiPayloadValidation.IsValidCalendarTime(time.Year, time.Month, time.Day, time.Hour,
            time.Minute, time.Second);
    }
}
