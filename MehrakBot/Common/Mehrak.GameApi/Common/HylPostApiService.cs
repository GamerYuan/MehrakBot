using System.Text.Json;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common.Types;
using Microsoft.Extensions.Logging;

namespace Mehrak.GameApi.Common;

internal class HylPostApiService : IApiService<HylPost, HylPostApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<HylPostApiService> m_Logger;

    private const string Endpoint = "/community/post/wapi/getPostFull";

    public HylPostApiService(IHttpClientFactory httpClientFactory, ILogger<HylPostApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<HylPost>> GetAsync(HylPostApiContext context)
    {
        var requestUri = $"{HoYoLabDomains.BbsApi}/{Endpoint}?post_id={context.PostId}&scene=1";
        try
        {
            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(requestUri)
            };
            request.Headers.Add("X-Rpc-Language", context.Locale.ToLocaleString());

            m_Logger.LogInformation(LogMessages.OutboundHttpRequest, request.Method, requestUri);
            var response = await client.SendAsync(request);

            m_Logger.LogInformation(LogMessages.InboundHttpResponse, (int)response.StatusCode, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<HylPost>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while accessing HoYoLAB API", requestUri);
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<HylPostWrapper>>(await response.Content.ReadAsStreamAsync());

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

            m_Logger.LogInformation(LogMessages.SuccessfullyRetrievedData, requestUri, context.UserId);
            return Result<HylPost>.Success(json.Data.Post, requestUri: requestUri);
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                requestUri, context.UserId);
            return Result<HylPost>.Failure(StatusCode.BotError, "An error occurred while fetching wiki data.");
        }
    }
}
