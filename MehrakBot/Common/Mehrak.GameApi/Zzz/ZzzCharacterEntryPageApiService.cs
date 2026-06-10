#region

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ZzzCharacterEntryPageApiService> m_Logger;
    private readonly ICacheService m_Cache;
    private static readonly JsonSerializerOptions JsonOptions = new() { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    public ZzzCharacterEntryPageApiService(IHttpClientFactory httpClientFactory, ILogger<ZzzCharacterEntryPageApiService> logger,
        ICacheService cache)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
        m_Cache = cache;
    }

    public async Task<Result<ZzzCharacterEntryPageList>> GetAsync(ZzzCharacterEntryPageApiContext context, CancellationToken cancellationToken = default)
    {
        var cached = await m_Cache.GetAsync<ZzzCharacterEntryPageList>(CacheKey, cancellationToken);
        if (cached != null)
            return Result<ZzzCharacterEntryPageList>.Success(cached);

        try
        {
            var httpClient = m_HttpClientFactory.CreateClient("Default");

            var firstPageResult = await FetchPageAsync(httpClient, 1, cancellationToken);
            if (firstPageResult != null && !firstPageResult.IsSuccess)
                return firstPageResult;

            var allItems = firstPageResult?.Data?.List ?? [];

            var totalPages = (int)Math.Ceiling((double)(firstPageResult?.Data?.Total ?? 0) / PageSize);
            for (var page = 2; page <= totalPages; page++)
            {
                var pageResult = await FetchPageAsync(httpClient, page, cancellationToken);
                if (pageResult == null || !pageResult.IsSuccess)
                    return pageResult ?? Result<ZzzCharacterEntryPageList>.Failure(StatusCode.BotError, "Unexpected null result");

                allItems.AddRange(pageResult.Data!.List);
            }

            var result = new ZzzCharacterEntryPageList { List = allItems, Total = firstPageResult?.Data?.Total ?? allItems.Count };

            var cacheEntry = new CacheEntryBase<ZzzCharacterEntryPageList>(CacheKey, result, CacheExpiration);
            await m_Cache.SetAsync(cacheEntry, cancellationToken);

            return Result<ZzzCharacterEntryPageList>.Success(result);
        }
        catch (OperationCanceledException)
        {
            return Result<ZzzCharacterEntryPageList>.FromCancellation(cancellationToken);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessages.ExceptionOccurred, Endpoint, context.UserId);
            return Result<ZzzCharacterEntryPageList>.Failure(StatusCode.BotError,
                "An error occurred while retrieving ZZZ entry page list");
        }
    }

    private async Task<Result<ZzzCharacterEntryPageList>?> FetchPageAsync(HttpClient httpClient, int pageNum, CancellationToken cancellationToken)
    {
        var payload = new
        {
            filters = Array.Empty<object>(),
            menu_id = MenuId,
            page_num = pageNum,
            page_size = PageSize,
            use_es = true
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = content
        };
        request.Headers.Add("X-Rpc-Wiki_app", "zzz");
        request.Headers.Add("X-Rpc-Language", "en-us");
        request.Headers.Add("Referer", "https://wiki.hoyolab.com/");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var apiResponse = JsonSerializer.Deserialize<ApiResponse<ZzzCharacterEntryPageList>>(responseBody, JsonOptions);

        if (apiResponse?.Retcode != 0 || apiResponse.Data == null)
        {
            m_Logger.LogWarning("Failed to get ZZZ entry page list: retcode={Retcode}, message={Message}",
                apiResponse?.Retcode, apiResponse?.Message);
            return Result<ZzzCharacterEntryPageList>.Failure(StatusCode.ExternalServerError, apiResponse?.Message ?? "Unknown error");
        }

        return Result<ZzzCharacterEntryPageList>.Success(apiResponse.Data);
    }
}
