#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinTheaterApiService : IApiService<GameApiResponse<GenshinTheaterResponseData>, BaseHoYoApiContext>
{
    private const string ErrorMessage = "An error occurred while retrieving Imaginarium Theater data";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinTheaterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/role_combat";

    public GenshinTheaterApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinTheaterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<GenshinTheaterResponseData>>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<GenshinTheaterResponseData>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&need_detail=true";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var request = new HoYoLabRequest
            {
                RequestUri = requestUri,
                Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")]
            };

            return await HoYoLabApi.ExecuteAsync<GenshinTheaterResponseData,
                GameApiResponse<GenshinTheaterResponseData>>(
                request,
                new HoYoLabEnvelopePolicy<GenshinTheaterResponseData>
                {
                    UnknownRetcode = ErrorMessage
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                payload =>
                {
                    if (!payload.IsUnlock)
                    {
                        m_Logger.LogInformation(LogMessages.FeatureNotUnlocked, "Imaginarium Theater",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<GenshinTheaterResponseData>>.Success(
                            GameApiResponse<GenshinTheaterResponseData>.Locked(), requestUri: requestUri));
                    }

                    if (payload.Data is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<GenshinTheaterResponseData>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    if (payload.Data.Count == 0 && payload.TarotCardState is null)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Imaginarium Theater",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<GenshinTheaterResponseData>>.Success(
                            GameApiResponse<GenshinTheaterResponseData>.Empty(), requestUri: requestUri));
                    }

                    if (!HasValidDetailData(payload.Data))
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<GenshinTheaterResponseData>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    return Task.FromResult(Result<GameApiResponse<GenshinTheaterResponseData>>.Success(
                        GameApiResponse<GenshinTheaterResponseData>.Available(payload), requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<GenshinTheaterResponseData>>.FromCancellation(cancellationToken,
                requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GameApiResponse<GenshinTheaterResponseData>>.Failure(StatusCode.BotError,
                ErrorMessage, requestUri);
        }
    }

    private static bool HasValidDetailData(List<GenshinTheaterInformation> records)
    {
        foreach (var record in records)
        {
            if (record is null)
                return false;

            if (!record.HasDetailData)
                continue;

            if (record.Detail is null || record.Detail.RoundsData is null ||
                record.Detail.RoundsData.Count == 0 ||
                record.Detail.FightStatistic is null || record.Stat is null ||
                record.Stat.GetMedalRoundList is null || record.Schedule is null)
                return false;

            if (!GameApiPayloadValidation.IsUnixSeconds(record.Schedule.StartTime) ||
                !GameApiPayloadValidation.IsUnixSeconds(record.Schedule.EndTime))
                return false;

            var fightStatistic = record.Detail.FightStatistic;
            if (fightStatistic.MaxDefeatAvatar is null || fightStatistic.MaxDamageAvatar is null ||
                fightStatistic.MaxTakeDamageAvatar is null || fightStatistic.TotalCoinConsumed is null ||
                fightStatistic.ShortestAvatarList is null ||
                !GameApiPayloadValidation.HasNoNullEntries(fightStatistic.ShortestAvatarList))
                return false;

            if (record.Detail.RoundsData.Any(round => round is null || round.Avatars is null ||
                !GameApiPayloadValidation.HasNoNullEntries(round.Avatars) ||
                round.SplendourBuff is null || round.SplendourBuff.Summary is null ||
                round.SplendourBuff.Buffs is null ||
                !GameApiPayloadValidation.HasNoNullEntries(round.SplendourBuff.Buffs) ||
                round.SplendourBuff.Buffs.Any(buff => buff.Name is null)))
                return false;
        }

        return true;
    }
}
