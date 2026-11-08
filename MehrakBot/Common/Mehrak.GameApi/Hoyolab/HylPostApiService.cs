using Mehrak.Domain.Shared.Abstractions;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.Shared.Models;
using Mehrak.Domain.Shared.Services;
using Mehrak.GameApi.Shared;
using Microsoft.Extensions.Logging;

namespace Mehrak.GameApi.Hoyolab;

public class HylPostApiService : IApiService<HylPost, HylPostApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HylPostApiService> m_Logger;

    private const string Endpoint = "community/post/wapi/getPostFull";

    public HylPostApiService(IHttpClientFactory httpClientFactory, ILogger<HylPostApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HylPost>> GetAsync(HylPostApiContext context, CancellationToken cancellationToken = default)
    {
        var requestUri = $"{HoYoLabDomains.BbsApi}/{Endpoint}?post_id={context.PostId}&scene=1";
        try
        {
            using var scope = new HoYoLabRequestScope(cancellationToken);

            return await HoYoLabApi.ExecuteAsync(
                new HoYoLabRequest
                {
                    RequestUri = requestUri,
                    RequiresCredentials = false,
                    Headers =
                    [
                        new KeyValuePair<string, string>("X-Rpc-Language", context.Locale.ToLocaleString()),
                        new KeyValuePair<string, string>("X-Rpc-Show-Translated", "true"),
                        new KeyValuePair<string, string>("X-Rpc-Client_type", "4"),
                        new KeyValuePair<string, string>("X-Rpc-App_version", "4.9.0"),
                        new KeyValuePair<string, string>("X-Rpc-Lsrag", "")
                    ],
                    Failures = new HoYoLabFailureMessages
                    {
                        Parse = "Failed to parse response from HoYoLAB API",
                        NonSuccessStatus = "An error occurred while accessing HoYoLAB API"
                    }
                },
                new HoYoLabEnvelopePolicy<HylPostWrapper>
                {
                    AuthRetcode = null,
                    HasData = data => data.Post is not null,
                    UnknownRetcode = "An error occurred while accessing HoYoLAB API",
                    EmptyData = "HoYoLAB API returned empty data"
                },
                new HoYoLabCredentials(context.UserId, 0, string.Empty),
                m_HttpClientFactory, m_Logger, scope.Token,
                data => Task.FromResult(Result<HylPost>.Success(data.Post, requestUri: requestUri)));
        }
        catch (OperationCanceledException)
        {
            return Result<HylPost>.FromCancellation(cancellationToken, requestUri);
        }
        catch (Exception e)
        {
            GameApiLog.LogException(m_Logger, requestUri, context.UserId, e);
            return Result<HylPost>.Failure(StatusCode.BotError, "An error occurred while fetching HoYoLAB Post data.",
                requestUri);
        }
    }
}

public class HylPostApiContext : IApiContext
{
    public ulong UserId { get; }
    public long PostId { get; }
    public WikiLocales Locale { get; }

    public HylPostApiContext(ulong userId, long postId, WikiLocales locale = WikiLocales.EN)
    {
        UserId = userId;
        PostId = postId;
        Locale = locale;
    }
}
