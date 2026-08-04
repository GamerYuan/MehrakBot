using Grpc.Core;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain.Shared.Services;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Application.Shared.Services;

internal class PortraitMatcherGrpcClient : IPortraitMatcher
{
    private readonly Proto.ImageProcessorService.ImageProcessorServiceClient m_Client;
    private readonly ILogger<PortraitMatcherGrpcClient> m_Logger;

    public PortraitMatcherGrpcClient(
        Proto.ImageProcessorService.ImageProcessorServiceClient client,
        ILogger<PortraitMatcherGrpcClient> logger)
    {
        m_Client = client;
        m_Logger = logger;
    }

    public async Task<(bool IsMatch, float Confidence)> MatchAsync(
        byte[] referenceImage, byte[] candidateImage, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new Proto.MatchImageRequest
            {
                ReferenceImage = Google.Protobuf.ByteString.CopyFrom(referenceImage),
                CandidateImage = Google.Protobuf.ByteString.CopyFrom(candidateImage)
            };

            var response = await m_Client.MatchImageAsync(
                request,
                deadline: DateTime.UtcNow.AddSeconds(IApiService.MaxTimeoutSeconds),
                cancellationToken: cancellationToken);
            return (response.IsMatch, response.Confidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            throw new OperationCanceledException("Portrait matching was cancelled", ex, cancellationToken);
        }
        catch (RpcException ex)
        {
            m_Logger.LogError(ex, "Failed to match portrait image through ImageProcessor");
            throw;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to match portrait image");
            return (false, 0f);
        }
    }
}

internal class ImageFetcher : IImageFetcher
{
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ImageFetcher> m_Logger;

    public ImageFetcher(IHttpClientFactory httpClientFactory, ILogger<ImageFetcher> logger)
    {
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<byte[]?> FetchBytesAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var client = m_HttpClientFactory.CreateClient();
            using var response = await client.GetAsync(url, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogWarning("Failed to fetch image bytes from {Url}: {StatusCode}", url, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogWarning("Image fetch timed out for {Url}", url);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or InvalidOperationException or IOException)
        {
            m_Logger.LogWarning(ex, "Failed to fetch image bytes from {Url}", url);
            return null;
        }
    }
}
