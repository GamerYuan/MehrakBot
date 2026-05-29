#region

using System.Text.Json;
using Mehrak.Domain.Models;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Common;
using Mehrak.GameApi.Common.Types;
using Mehrak.GameApi.Genshin.Types;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Genshin;

internal class GenshinTheaterApiService : IApiService<GenshinTheaterInformation, BaseHoYoApiContext>
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<GenshinTheaterApiService> m_Logger;

    private static readonly string ApiEndpoint = "/event/game_record/genshin/api/role_combat";

    public GenshinTheaterApiService(IHttpClientFactory httpClientFactory, ILogger<GenshinTheaterApiService> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<Result<GenshinTheaterInformation>> GetAsync(BaseHoYoApiContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.GameUid) || string.IsNullOrEmpty(context.Region))
        {
            m_Logger.LogError(LogMessages.InvalidRegionOrUid);
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BadParameter,
                "Game UID or region is null or empty");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var requestUri =
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}?role_id={context.GameUid}&server={context.Region}&need_detail=true";

            m_Logger.LogInformation(LogMessages.PreparingRequest, requestUri);

            var client = m_HttpClientFactory.CreateClient("Default");
            HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.Add("Cookie", $"ltuid_v2={context.LtUid}; ltoken_v2={context.LToken}");

            var response = await client.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, requestUri);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data", requestUri);
            }

            var json = await JsonSerializer.DeserializeAsync<ApiResponse<GenshinTheaterResponseData>>(
                await response.Content.ReadAsStreamAsync(timeoutCts.Token), (JsonSerializerOptions?)null, timeoutCts.Token);

            if (json?.Data == null)
            {
                m_Logger.LogError(LogMessages.EmptyResponseData, requestUri, context.UserId);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data", requestUri);
            }

            m_Logger.LogInformation(LogMessages.InboundHttpResponseWithRetcode, (int)response.StatusCode, requestUri, json.Retcode, context.UserId);

            if (json.Retcode == 10001)
            {
                m_Logger.LogError(LogMessages.InvalidCredentials, context.UserId);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.Unauthorized,
                    "Invalid HoYoLAB UID or Cookies. Please authenticate again", requestUri);
            }

            if (json.Retcode != 0)
            {
                m_Logger.LogError(LogMessages.UnknownRetcode, json.Retcode, context.UserId, requestUri, json);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "An error occurred while retrieving Imaginarium Theater data", requestUri);
            }

            if (!json.Data.IsUnlock)
            {
                m_Logger.LogInformation(LogMessages.FeatureNotUnlocked, "Imaginarium Theater", context.UserId);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.Unauthorized,
                    "Imaginarium Theater is not unlocked yet", requestUri);
            }

            var theaterInfo = json.Data.Data;
            if (theaterInfo == null || theaterInfo.Count == 0)
            {
                m_Logger.LogError(LogMessages.DataNotFoundForFeature, "Imaginarium Theater", context.UserId);
                return Result<GenshinTheaterInformation>.Failure(StatusCode.ExternalServerError,
                    "No Imaginarium Theater data found", requestUri);
            }

            return Result<GenshinTheaterInformation>.Success(json.Data.Data[0], requestUri: requestUri);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<GenshinTheaterInformation>.Failure(StatusCode.Cancelled, "Request was cancelled");
        }
        catch (OperationCanceledException)
        {
            return Result<GenshinTheaterInformation>.Failure(StatusCode.Timeout, "Request to HoYoLAB timed out");
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessages.ExceptionOccurred,
                $"{HoYoLabDomains.PublicApi}{ApiEndpoint}", context.UserId);
            return Result<GenshinTheaterInformation>.Failure(StatusCode.BotError,
                "An error occurred while retrieving Imaginarium Theater data");
        }
    }
}
