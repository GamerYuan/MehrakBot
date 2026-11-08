#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.DailyCheckIn;

public class DailyCheckInApiService : IApiService<CheckInStatus, CheckInApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<DailyCheckInApiService> m_Logger;

    private static readonly Dictionary<Game, string> CheckInUrls = new()
    {
        { Game.Genshin, $"{HoYoLabDomains.GenshinApi}/event/sol/sign" },
        { Game.HonkaiStarRail, $"{HoYoLabDomains.PublicApi}/event/luna/hkrpg/os/sign" },
        { Game.ZenlessZoneZero, $"{HoYoLabDomains.PublicApi}/event/luna/zzz/os/sign" },
        { Game.HonkaiImpact3, $"{HoYoLabDomains.PublicApi}/event/mani/sign" },
        { Game.TearsOfThemis, $"{HoYoLabDomains.PublicApi}/event/luna/nxx/os/sign" }
    };

    private static readonly Dictionary<Game, string> CheckInActIds = new()
    {
        { Game.Genshin, "e202102251931481" },
        { Game.HonkaiStarRail, "e202303301540311" },
        { Game.ZenlessZoneZero, "e202406031448091" },
        { Game.HonkaiImpact3, "e202110291205111" },
        { Game.TearsOfThemis, "e202202281857121" }
    };

    public DailyCheckInApiService(IHttpClientFactory httpClientFactory, ILogger<DailyCheckInApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<CheckInStatus>> GetAsync(CheckInApiContext context, CancellationToken cancellationToken = default)
    {
        if (!CheckInUrls.TryGetValue(context.Game, out var requestUri) ||
            !CheckInActIds.TryGetValue(context.Game, out var actId))
        {
            m_Logger.LogError("Invalid check-in type: {Type}", context.Game);
            return Result<CheckInStatus>.Failure(StatusCode.BadParameter, "Invalid check-in type");
        }

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            CheckInApiPayload requestBody = new() { ActId = actId };

            var headers = new List<KeyValuePair<string, string>>();
            if (context.Game == Game.ZenlessZoneZero)
                headers.Add(new KeyValuePair<string, string>("X-Rpc-Signgame", "zzz"));

            var envelope = await HoYoLabApi.SendEnvelopeAsync<object>(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Method = HttpMethod.Post,
                    Body = requestBody,
                    Headers = headers,
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please re-authenticate",
                        Parse = "An unknown error occurred during check-in",
                        NonSuccessStatus = "An unknown error occurred during check-in"
                    }
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token, HoYoLabRetcodes.AccountAuth);

            if (!envelope.IsSuccess)
                return Result<CheckInStatus>.Failure(envelope.StatusCode, envelope.ErrorMessage, envelope.RequestUri);

            var json = envelope.Data;

            switch (json.Retcode)
            {
                case HoYoLabRetcodes.AccountAuth:
                    m_Logger.LogError(LogMessages.InvalidCredentials, GameApiLog.ContextId(context.UserId));
                    return Result<CheckInStatus>.Failure(StatusCode.Unauthorized,
                        "Invalid HoYoLAB UID or Cookies. Please re-authenticate", requestUri);

                case -5003:
                    m_Logger.LogInformation(LogMessages.AlreadyCheckedIn, GameApiLog.ContextId(context.UserId), context.Game.ToString());
                    return Result<CheckInStatus>.Success(CheckInStatus.AlreadyCheckedIn, requestUri: requestUri);

                case 0:
                    return Result<CheckInStatus>.Success(CheckInStatus.Success, requestUri: requestUri);

                case -10002:
                    m_Logger.LogInformation(LogMessages.NoValidProfile, GameApiLog.ContextId(context.UserId), context.Game.ToString());
                    return Result<CheckInStatus>.Success(CheckInStatus.NoValidProfile, requestUri: requestUri);

                default:
                    m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, GameApiLog.ContextId(context.UserId),
                        GameApiLog.Endpoint(requestUri), GameApiLog.SafeQueryParameters(requestUri),
                        GameApiLog.RenderBody(json));
                    return Result<CheckInStatus>.Failure(StatusCode.ExternalServerError,
                        "An unknown error occurred during check-in", requestUri);
            }
        }
        catch (OperationCanceledException)
        {
            return Result<CheckInStatus>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, requestUri, context.UserId, e);
            return Result<CheckInStatus>.Failure(StatusCode.BotError,
                "An error occurred during check-in", requestUri);
        }
    }

    private sealed class CheckInApiPayload
    {
        [JsonPropertyName("act_id")] public string ActId { get; set; } = string.Empty;
    }
}

public class CheckInApiContext : IApiContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }
    public Game Game { get; }

    public CheckInApiContext(ulong userId, ulong ltuid, string ltoken, Game game)
    {
        UserId = userId;
        LtUid = ltuid;
        LToken = ltoken;
        Game = game;
    }
}
