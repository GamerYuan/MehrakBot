#region

using System.Text;
using System.Text.Json;
using Mehrak.Domain.Cache;
using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
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
        var cached = await m_Cache.GetAsync<ZzzCharacterEntryPageList>(CacheKey, cancellationToken);
        if (cached != null)
            return Result<ZzzCharacterEntryPageList>.Success(cached);

        try
        {
            var payload = new
            {
                filters = Array.Empty<object>(),
                menu_id = MenuId,
                page_num = 1,
                page_size = 30,
                use_es = true
            };

            using var httpClient = m_HttpClientFactory.CreateClient();
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
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<ZzzCharacterEntryPageList>>(responseBody);

            if (apiResponse?.Retcode != 0 || apiResponse.Data == null)
            {
                m_Logger.LogWarning("Failed to get ZZZ entry page list: retcode={Retcode}, message={Message}",
                    apiResponse?.Retcode, apiResponse?.Message);
                return Result<ZzzCharacterEntryPageList>.Failure(StatusCode.ExternalServerError, apiResponse?.Message ?? "Unknown error");
            }

            var cacheEntry = new CacheEntryBase<ZzzCharacterEntryPageList>(CacheKey, apiResponse.Data, CacheExpiration);
            await m_Cache.SetAsync(cacheEntry, cancellationToken);

            return Result<ZzzCharacterEntryPageList>.Success(apiResponse.Data);
        }
        catch (Exception ex)
        {
            m_Logger.LogWarning(ex, "Error getting ZZZ entry page list");
            return Result<ZzzCharacterEntryPageList>.Failure(StatusCode.Timeout, ex.Message);
        }
    }
}
