#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinAbyssApiService : IApiService<GameApiResponse<GenshinAbyssInformation>, BaseHoYoApiContext>
{
    private const string ErrorMessage = "An error occurred while fetching Abyss information";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinAbyssApiService> m_Logger;
    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/spiralAbyss";

    public GenshinAbyssApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinAbyssApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<GenshinAbyssInformation>>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<GenshinAbyssInformation>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&schedule_type=1";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var request = new HoYoLabRequest
            {
                RequestUri = requestUri
            };

            return await HoYoLabApi.ExecuteAsync<GenshinAbyssInformation,
                GameApiResponse<GenshinAbyssInformation>>(
                request,
                new HoYoLabEnvelopePolicy<GenshinAbyssInformation>
                {
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                payload =>
                {
                    if (payload.Floors is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<GenshinAbyssInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    if (payload.Floors.Count == 0)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Spiral Abyss",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<GenshinAbyssInformation>>.Success(
                            GameApiResponse<GenshinAbyssInformation>.Empty(), requestUri: requestUri));
                    }

                    if (!HasValidStructure(payload))
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<GenshinAbyssInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    return Task.FromResult(Result<GameApiResponse<GenshinAbyssInformation>>.Success(
                        GameApiResponse<GenshinAbyssInformation>.Available(payload), requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<GenshinAbyssInformation>>.FromCancellation(cancellationToken,
                requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GameApiResponse<GenshinAbyssInformation>>.Failure(StatusCode.BotError,
                ErrorMessage, requestUri);
        }
    }

    private static bool HasValidStructure(GenshinAbyssInformation payload)
    {
        if (!GameApiPayloadValidation.IsUnixSeconds(payload.StartTime) ||
            !GameApiPayloadValidation.IsUnixSeconds(payload.EndTime))
            return false;

        if (payload.RevealRank is null || payload.DefeatRank is null || payload.DamageRank is null ||
            payload.TakeDamageRank is null || payload.NormalSkillRank is null || payload.EnergySkillRank is null)
            return false;

        if (payload.DefeatRank.Count == 0 || payload.DamageRank.Count == 0 ||
            payload.TakeDamageRank.Count == 0 || payload.NormalSkillRank.Count == 0 ||
            payload.EnergySkillRank.Count == 0)
            return false;

        if (!GameApiPayloadValidation.HasNoNullEntries(payload.RevealRank) ||
            !GameApiPayloadValidation.HasNoNullEntries(payload.DefeatRank) ||
            !GameApiPayloadValidation.HasNoNullEntries(payload.DamageRank) ||
            !GameApiPayloadValidation.HasNoNullEntries(payload.TakeDamageRank) ||
            !GameApiPayloadValidation.HasNoNullEntries(payload.NormalSkillRank) ||
            !GameApiPayloadValidation.HasNoNullEntries(payload.EnergySkillRank))
            return false;

        if (payload.Floors is null || !GameApiPayloadValidation.HasNoNullEntries(payload.Floors))
            return false;

        return payload.Floors.All(floor => floor.Levels is not null &&
            GameApiPayloadValidation.HasNoNullEntries(floor.Levels) &&
            floor.Levels.All(level => level.Battles is not null &&
                GameApiPayloadValidation.HasNoNullEntries(level.Battles) &&
                level.Battles.All(battle => battle.Avatars is not null &&
                    GameApiPayloadValidation.HasNoNullEntries(battle.Avatars))));
    }
}
