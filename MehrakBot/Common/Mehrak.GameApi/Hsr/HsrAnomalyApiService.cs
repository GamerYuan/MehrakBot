#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Hsr.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Hsr;

internal class HsrAnomalyApiService : IApiService<GameApiResponse<HsrAnomalyInformation>, BaseHoYoApiContext>
{
    private const string ErrorMessage =
        "An unknown error occurred when accessing HoYoLAB API. Please try again later";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HsrAnomalyApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/hkrpg/api/challenge_peak";

    public HsrAnomalyApiService(IHttpClientFactory httpClientFactory, ILogger<HsrAnomalyApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<HsrAnomalyInformation>>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<HsrAnomalyInformation>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&schedule_type=1";

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

            return await HoYoLabApi.ExecuteAsync<HsrAnomalyInformation,
                GameApiResponse<HsrAnomalyInformation>>(
                request,
                new HoYoLabEnvelopePolicy<HsrAnomalyInformation>
                {
                    UnknownRetcode = ErrorMessage
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                payload =>
                {
                    if (payload.ChallengeRecords is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<HsrAnomalyInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    if (payload.ChallengeRecords.Count == 0)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Anomaly Arbitration",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<HsrAnomalyInformation>>.Success(
                            GameApiResponse<HsrAnomalyInformation>.Empty(), requestUri: requestUri));
                    }

                    if (!HasValidStructure(payload))
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<HsrAnomalyInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    return Task.FromResult(Result<GameApiResponse<HsrAnomalyInformation>>.Success(
                        GameApiResponse<HsrAnomalyInformation>.Available(payload), requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<HsrAnomalyInformation>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GameApiResponse<HsrAnomalyInformation>>.Failure(StatusCode.BotError,
                "An error occurred while fetching Anomaly Arbitration information", requestUri);
        }
    }

    private static bool HasValidStructure(HsrAnomalyInformation payload)
    {
        if (payload.BestRecord is null || payload.BestRecord.RankIcon is null)
            return false;

        if (!GameApiPayloadValidation.HasNoNullEntries(payload.ChallengeRecords))
            return false;

        foreach (var record in payload.ChallengeRecords)
        {
            var group = record.Group;
            if (group is null || group.BeginTime is null || group.EndTime is null ||
                !HasValidCalendarTime(group.BeginTime) || !HasValidCalendarTime(group.EndTime) ||
                record.BossInfo is null || record.BossInfo.Name is null ||
                record.MobInfo is null || !GameApiPayloadValidation.HasNoNullEntries(record.MobInfo) ||
                record.MobRecords is null || !GameApiPayloadValidation.HasNoNullEntries(record.MobRecords))
                return false;

            if (record.MobRecords.Any(mobRecord =>
                    !GameApiPayloadValidation.HasNoNullEntries(mobRecord.Avatars) ||
                    !record.MobInfo.Any(mobInfo => mobInfo.MazeId == mobRecord.MazeId)))
                return false;

            if (record.BossRecord is { } bossRecord &&
                (bossRecord.Buff is null ||
                 !GameApiPayloadValidation.HasNoNullEntries(bossRecord.Avatars)))
                return false;
        }

        return payload.ChallengeRecords[0].Group.GameVersion is not null;
    }

    private static bool HasValidCalendarTime(ScheduleTime time)
    {
        return GameApiPayloadValidation.IsValidCalendarTime(time.Year, time.Month, time.Day, time.Hour,
            time.Minute);
    }
}
