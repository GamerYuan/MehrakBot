#region

using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Genshin.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinStygianApiService : IApiService<GameApiResponse<GenshinStygianInformation>, BaseHoYoApiContext>
{
    private const string ErrorMessage = "An error occurred while retrieving Stygian Onslaught data";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinStygianApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/hard_challenge";

    public GenshinStygianApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinStygianApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GameApiResponse<GenshinStygianInformation>>> GetAsync(BaseHoYoApiContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GameApiResponse<GenshinStygianInformation>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&need_detail=true";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var request = new HoYoLabRequest
            {
                RequestUri = requestUri
            };

            return await HoYoLabApi.ExecuteAsync<GenshinStygianInformation,
                GameApiResponse<GenshinStygianInformation>>(
                request,
                new HoYoLabEnvelopePolicy<GenshinStygianInformation>
                {
                    UnknownRetcode = ErrorMessage
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                payload =>
                {
                    if (!payload.IsUnlock)
                    {
                        m_Logger.LogInformation(LogMessages.FeatureNotUnlocked, "Stygian Onslaught",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<GenshinStygianInformation>>.Success(
                            GameApiResponse<GenshinStygianInformation>.Locked(), requestUri: requestUri));
                    }

                    if (payload.Data is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<GenshinStygianInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    if (payload.Data.Count == 0)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Stygian Onslaught",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<GenshinStygianInformation>>.Success(
                            GameApiResponse<GenshinStygianInformation>.Empty(), requestUri: requestUri));
                    }

                    var record = payload.Data[0];
                    if (record is null || record.Single is null)
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<GenshinStygianInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    if (!record.Single.HasData)
                    {
                        m_Logger.LogInformation(LogMessages.DataNotFoundForFeature, "Stygian Onslaught",
                            GameApiLog.ContextId(context.UserId));
                        return Task.FromResult(Result<GameApiResponse<GenshinStygianInformation>>.Success(
                            GameApiResponse<GenshinStygianInformation>.Empty(), requestUri: requestUri));
                    }

                    if (record.Schedule is null ||
                        !GameApiPayloadValidation.IsUnixSeconds(record.Schedule.StartTime) ||
                        !GameApiPayloadValidation.IsUnixSeconds(record.Schedule.EndTime) ||
                        record.Single.Challenge is null ||
                        !GameApiPayloadValidation.HasNoNullEntries(record.Single.Challenge) ||
                        record.Single.Challenge.Any(challenge => challenge.Teams is null ||
                            !GameApiPayloadValidation.HasNoNullEntries(challenge.Teams) ||
                            challenge.BestAvatar is null ||
                            !GameApiPayloadValidation.HasNoNullEntries(challenge.BestAvatar) ||
                            challenge.Monster is null))
                        return Task.FromResult(HoYoLabApi.InvalidEndpointData<GenshinStygianInformation>(
                            requestUri, m_Logger, context.UserId, ErrorMessage));

                    return Task.FromResult(Result<GameApiResponse<GenshinStygianInformation>>.Success(
                        GameApiResponse<GenshinStygianInformation>.Available(payload), requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<GameApiResponse<GenshinStygianInformation>>.FromCancellation(cancellationToken,
                requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId, e);
            return Result<GameApiResponse<GenshinStygianInformation>>.Failure(StatusCode.BotError,
                ErrorMessage, requestUri);
        }
    }
}
