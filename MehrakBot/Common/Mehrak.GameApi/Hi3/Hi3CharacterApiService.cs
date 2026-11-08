using Mehrak.Domain.Cache;
using Mehrak.Domain.Character;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Hi3.Types;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.GameApi.Hi3;

internal class Hi3CharacterApiService : ICharacterApiService<Hi3CharacterDetail, Hi3CharacterDetail, CharacterApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ICacheService m_Cache;
    private readonly ILogger<Hi3CharacterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/game_record/honkai3rd/api/characters";
    private const int CacheExpirationMinutes = 10;

    public Hi3CharacterApiService(
        IHttpClientFactory httpClientFactory,
        ICacheService cache,
        ILogger<Hi3CharacterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Cache = cache;
        m_Logger = logger;
    }

    public async Task<Result<IEnumerable<Hi3CharacterDetail>>> GetAllCharactersAsync(CharacterApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Region) || string.IsNullOrEmpty(context.GameUid))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        var requestUri =
            $"{HoYoLabDomains.BbsApi}{ApiEndpoint}?server={context.Region}&role_id={context.GameUid}";

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var cacheKey = $"hi3_characters_{context.Region}_{context.GameUid}";
            var cachedData = await m_Cache.GetAsync<IEnumerable<Hi3CharacterDetail>>(cacheKey, scope.Token);

            // Try to get data from cache first
            if (cachedData != null)
            {
                return Result<IEnumerable<Hi3CharacterDetail>>.Success(cachedData);
            }

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
                    ],
                    Failures = new HoYoLabFailureMessages
                    {
                        Unauthorized = "Invalid HoYoLAB UID or Cookies. Please authenticate again.",
                        Parse = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                        NonSuccessStatus = "Failed to retrieve character information"
                    }
                },
                new HoYoLabEnvelopePolicy<Hi3CharacterList>
                {
                    HasData = data => data.Characters is { Count: > 0 },
                    UnknownRetcode = "An unknown error occurred when accessing HoYoLAB API. Please try again later",
                    EmptyData = "Failed to retrieve character information"
                },
                new HoYoLabCredentials(context.UserId, context.LtUid, context.LToken),
                m_HttpClientFactory, m_Logger, scope.Token,
                async data =>
                {
                    List<Hi3CharacterDetail> result = [.. data.Characters.Select(x => x.Character)];

                    await m_Cache.SetAsync(
                        new CharacterListCacheEntry<Hi3CharacterDetail>(cacheKey, result,
                            TimeSpan.FromMinutes(CacheExpirationMinutes)), scope.Token);

                    return Result<IEnumerable<Hi3CharacterDetail>>.Success(result, requestUri: requestUri);
                });
        }
        catch (OperationCanceledException)
        {
            return Result<IEnumerable<Hi3CharacterDetail>>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, $"{HoYoLabDomains.BbsApi}{ApiEndpoint}", context.UserId, e);
            return Result<IEnumerable<Hi3CharacterDetail>>.Failure(StatusCode.BotError,
                "An error occurred while retrieving character information", requestUri);
        }
    }

    /// <summary>
    /// Stub! DO NOT USE!
    /// </summary>
    public Task<Result<Hi3CharacterDetail>> GetCharacterDetailAsync(CharacterApiContext context, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
