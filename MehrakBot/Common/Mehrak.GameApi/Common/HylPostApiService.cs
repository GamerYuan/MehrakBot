using System.Text.Json;
using Mehrak.Domain.Enums;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.GameApi.Common;

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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("X-Rpc-Language", context.Locale.ToLocaleString());
            request.Headers.Add("X-Rpc-Show-Translated", "true");
            request.Headers.Add("X-Rpc-Client_type", "4");
            request.Headers.Add("X-Rpc-App_version", "4.9.0");
            request.Headers.Add("X-Rpc-Lsrag", "");

            var response = await client.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<HylPost>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoLAB API", requestUri);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            var json = await JsonSerializer.DeserializeAsync<ApiResponse<HylPostWrapper>>(stream, (JsonSerializerOptions?)null, timeoutCts.Token);

            if (json == null)
            {
                m_Logger.LogError(LogMessages.FailedToParseResponse, requestUri, context.UserId);
                return Result<HylPost>.Failure(StatusCode.ExternalServerError,
                    "Failed to parse response from HoYoLAB API", requestUri);
            }

            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<HylPost>.Failure(StatusCode.ExternalServerError,
                    $"HoYoLAB API returned error code {json.Retcode} with message: {json.Message}", requestUri);
            }

            if (json.Data == null || json.Data.Post == null)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<HylPost>.Failure(StatusCode.ExternalServerError,
                    "HoYoLAB API returned empty data", requestUri);
            }

            return Result<HylPost>.Success(json.Data.Post, requestUri: requestUri);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<HylPost>.Failure(StatusCode.Cancelled, "Request was cancelled");
        }
        catch (OperationCanceledException)
        {
            return Result<HylPost>.Failure(StatusCode.Timeout, "Request to HoYoLAB timed out");
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                requestUri, context.UserId);
            return Result<HylPost>.Failure(StatusCode.BotError, "An error occurred while fetching HoYoLAB Post data.");
        }
    }
}
