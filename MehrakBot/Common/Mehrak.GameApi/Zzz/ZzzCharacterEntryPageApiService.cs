#region

using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Mehrak.GameApi.Shared.Types;
using Mehrak.GameApi.Zzz.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Zzz;

public class ZzzCharacterEntryPageApiContext : IApiContext
{
    public ulong UserId { get; }

    public ZzzCharacterEntryPageApiContext(ulong userId)
    {
        UserId = userId;
    }
}

internal class ZzzCharacterEntryPageApiService : IApiService<ZzzCharacterEntryPageList, ZzzCharacterEntryPageApiContext>
{
    private const string MenuId = "8";
    private const string CacheKey = "zzz_character_entry_pages";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);
    private const string Endpoint = $"{HoYoLabDomains.WikiActApi}/zzz/wapi/get_entry_page_list";
    private const int PageSize = 50;

    /// <summary>Controlled failure copy; upstream text is never shown to users.</summary>
    private const string FailureMessage = "An error occurred while retrieving ZZZ entry page list";

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzCharacterEntryPageApiService> m_Logger;
    private readonly ICacheService m_Cache;

    public ZzzCharacterEntryPageApiService(IHttpClientFactory httpClientFactory, ILogger<ZzzCharacterEntryPageApiService> logger,
        ICacheService cache)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
        m_Cache = cache;
    }

    public async Task<Result<ZzzCharacterEntryPageList>> GetAsync(ZzzCharacterEntryPageApiContext context, CancellationToken cancellationToken = default)
    {
        const string requestUri = Endpoint;

        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            var cached = await m_Cache.GetAsync<ZzzCharacterEntryPageList>(CacheKey, scope.Token);
            if (cached != null)
                return Result<ZzzCharacterEntryPageList>.Success(cached);

            var firstPageResult = await FetchPageAsync(1, scope.Token);
            if (!firstPageResult.IsSuccess)
                return firstPageResult;

            var firstPage = firstPageResult.Data;
            var allItems = firstPage.List;

            var totalPages = (int)Math.Ceiling((double)firstPage.Total / PageSize);
            for (var page = 2; page <= totalPages; page++)
            {
                var pageResult = await FetchPageAsync(page, scope.Token);
                if (!pageResult.IsSuccess)
                    return pageResult;

                allItems.AddRange(pageResult.Data.List);
            }

            var result = new ZzzCharacterEntryPageList { List = allItems, Total = firstPage.Total };

            var cacheEntry = new CacheEntryBase<ZzzCharacterEntryPageList>(CacheKey, result, CacheExpiration);
            await m_Cache.SetAsync(cacheEntry, scope.Token);

            return Result<ZzzCharacterEntryPageList>.Success(result, requestUri: requestUri);
        }
        catch (OperationCanceledException)
        {
            return Result<ZzzCharacterEntryPageList>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception ex)
        {
            GameApiLog.LogException(m_Logger, Endpoint, context.UserId, ex);
            return Result<ZzzCharacterEntryPageList>.Failure(StatusCode.BotError, FailureMessage, requestUri);
        }
    }

    private Task<Result<ZzzCharacterEntryPageList>> FetchPageAsync(int pageNum, CancellationToken cancellationToken)
    {
        var payload = new
        {
            filters = Array.Empty<object>(),
            menu_id = MenuId,
            page_num = pageNum,
            page_size = PageSize,
            use_es = true
        };

        return HoYoLabApi.ExecuteAsync(
            new HoYoLabRequest
            {
                RequestUri = Endpoint,
                Method = HttpMethod.Post,
                Body = payload,
                RequiresCredentials = false,
                Headers =
                [
                    new KeyValuePair<string, string>("X-Rpc-Wiki_app", "zzz"),
                    new KeyValuePair<string, string>("X-Rpc-Language", "en-us"),
                    new KeyValuePair<string, string>("Referer", "https://wiki.hoyolab.com/")
                ]
            },
            new HoYoLabEnvelopePolicy<ZzzCharacterEntryPageList>
            {
                AuthRetcode = null,
                HasData = data => data.List is not null,
                UnknownRetcode = FailureMessage
            },
            new HoYoLabCredentials(0, 0, string.Empty),
            m_HttpClientFactory, m_Logger, cancellationToken);
    }
}
