#region

using System.Text.Json.Serialization;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.GameRecord;

public class GameRecordApiService : IApiService<IEnumerable<GameRecordDto>, GameRecordApiContext>
{
    private const string GameRecordApiPath = "/event/game_record/card/wapi/getGameRecordCard";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GameRecordApiService> m_Logger;

    public GameRecordApiService(IHttpClientFactory httpClientFactory, ILogger<GameRecordApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<GameRecordDto>>> GetAsync(GameRecordApiContext context, CancellationToken cancellationToken = default)
    {
        var requestUri = $"{HoYoLabDomains.PublicApi}{GameRecordApiPath}?uid={context.LtUid}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    Headers = [new KeyValuePair<string, string>("X-Rpc-Language", "en-us")],
                    CookieFormat = "ltoken_v2={1}; ltuid_v2={0}",
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please re-authenticate",
                        Parse = "An error occurred",
                        NonSuccessStatus = "An error occurred"
                    }
                },
                new HoYoLabEnvelopePolicy<UserData>
                {
                    AuthRetcode = HoYoLabRetcodes.AccountAuth,
                    HasData = data => data.List is not null,
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "An error occurred"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                data =>
                {
                    var result = data.List.Select(x => new GameRecordDto
                    {
                        GameId = x.GameId ?? 0,
                        Game = x.GameName switch
                        {
                            "Genshin Impact" => Game.Genshin,
                            "Honkai Impact 3rd" => Game.HonkaiImpact3,
                            "Honkai: Star Rail" => Game.HonkaiStarRail,
                            "Zenless Zone Zero" => Game.ZenlessZoneZero,
                            "Tears of Themis" => Game.TearsOfThemis,
                            _ => Game.Unsupported
                        },
                        HasRole = x.HasRole ?? false,
                        Nickname = x.Nickname ?? string.Empty,
                        Region = x.Region ?? string.Empty,
                        Level = x.Level ?? 0
                    });

                    return Task.FromResult(Result<IEnumerable<GameRecordDto>>.Success(result, requestUri: requestUri));
                });
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<GameRecordDto>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception ex)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.PublicApi}{GameRecordApiPath}", context.UserId, ex);
            return Result<IEnumerable<GameRecordDto>>.Failure(StatusCode.BotError, "An error occurred", requestUri);
        }
    }

    private sealed class UserData
    {
        [JsonPropertyName("list")] public required List<GameData> List { get; set; }
    }

    private sealed class GameDataEntry
    {
        [JsonPropertyName("name")] public required string Name { get; set; }

        [JsonPropertyName("type")] public int Type { get; set; }

        [JsonPropertyName("value")] public required string Value { get; set; }
    }

    private sealed class GameData
    {
        [JsonPropertyName("has_role")] public bool? HasRole { get; set; }

        [JsonPropertyName("game_id")] public int? GameId { get; set; }

        [JsonPropertyName("game_role_id")][GameApiSensitive] public required string GameRoleId { get; set; }

        [JsonPropertyName("nickname")][GameApiSensitive] public required string Nickname { get; set; }

        [JsonPropertyName("region")] public required string Region { get; set; }

        [JsonPropertyName("level")] public int? Level { get; set; }

        [JsonPropertyName("data")] public required List<GameDataEntry> Data { get; set; }

        [JsonPropertyName("region_name")] public required string RegionName { get; set; }

        [JsonPropertyName("game_name")] public required string GameName { get; set; }
    }
}

public class GameRecordApiContext : IApiContext
{
    public ulong UserId { get; }
    public ulong LtUid { get; }
    public string LToken { get; }

    public GameRecordApiContext(ulong userId, ulong ltuid, string ltoken)
    {
        UserId = userId;
        LtUid = ltuid;
        LToken = ltoken;
    }
}
