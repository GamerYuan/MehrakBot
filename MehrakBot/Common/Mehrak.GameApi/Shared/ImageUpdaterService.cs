#region

using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Abstractions;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Services;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Shared;

public class ImageUpdaterService : IImageUpdaterService
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IHttpClientFactory m_HttpClientFactory;
    private readonly ILogger<ImageUpdaterService> m_Logger;

    public ImageUpdaterService(IImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        ILogger<ImageUpdaterService> logger)
    {
        m_ImageRepository = imageRepository;
        m_HttpClientFactory = httpClientFactory;
        m_Logger = logger;
    }

    public async Task<bool> UpdateImageAsync(IImageData data, IImageProcessor processor, CancellationToken cancellationToken = default)
    {
        if (await m_ImageRepository.FileExistsAsync(data.Name, cancellationToken))
        {
            return true;
        }

        if (string.IsNullOrEmpty(data.Url))
        {
            m_Logger.LogWarning("{Name} has an empty URL, skipping download", data.Name);
            return false;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var client = m_HttpClientFactory.CreateClient();

            m_Logger.LogInformation(LogMessages.PreparingRequest, data.Url);
            using var response = await client.GetAsync(data.Url, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                m_Logger.LogError(LogMessages.NonSuccessStatusCode, response.StatusCode, data.Url);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);

            if (processor.ShouldProcess)
            {
                using var processedStream = await Task.Run(() => processor.ProcessImage(stream), cancellationToken);

                if (processedStream == Stream.Null || processedStream.Length == 0)
                {
                    m_Logger.LogWarning("Error processing {Name}, processed stream is null or empty", data.Name);
                    return false;
                }

                processedStream.Position = 0;
                await m_ImageRepository.UploadFileAsync(data.Name, processedStream, FileNameFormat.PngContentType, timeoutCts.Token);
            }
            else
            {
                stream.Position = 0;
                await m_ImageRepository.UploadFileAsync(data.Name, stream, FileNameFormat.PngContentType, timeoutCts.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogWarning("Image download timed out for {Url}", data.Url);
            return false;
        }
    }

    public async Task<bool> UpdateMultiImageAsync(IMultiImageData data, IMultiImageProcessor processor, CancellationToken cancellationToken = default)
    {
        if (await m_ImageRepository.FileExistsAsync(data.Name, cancellationToken))
        {
            return true;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(IApiService.MaxTimeoutSeconds));

            var client = m_HttpClientFactory.CreateClient();

            var allUrls = data.AdditionalUrls.Prepend(data.Url).Where(x => !string.IsNullOrEmpty(x)).ToList();

            m_Logger.LogInformation(LogMessages.PreparingRequest, string.Join(", ", allUrls));

            var responses = await allUrls.ToAsyncEnumerable().Select(async (x, token) =>
            {
                var r = await client.GetAsync(x, token);
                return r;
            }).ToListAsync(timeoutCts.Token);

            List<Stream> streams = [];

            try
            {

                if (responses.Any(x => !x.IsSuccessStatusCode))
                {
                    var failed = responses.Where(x => !x.IsSuccessStatusCode);
                    m_Logger.LogError("Failed to download {Name}, [\n{UrlError}\n]", data.Name, string.Join('\n',
                        failed.Select(x => $"{x.RequestMessage?.RequestUri}: {x.StatusCode}")));
                    return false;
                }

                streams.AddRange(await responses.ToAsyncEnumerable().Select(async (x, token) =>
                    await x.Content.ReadAsStreamAsync(token)).ToListAsync(cancellationToken: timeoutCts.Token));

                using var processedStream = processor.ProcessImage(streams);

                if (processedStream.Equals(Stream.Null) || processedStream.Length == 0)
                {
                    m_Logger.LogWarning("Error processing {Name}, processed stream is null or empty", data.Name);

                    return false;
                }

                processedStream.Position = 0;
                await m_ImageRepository.UploadFileAsync(data.Name, processedStream, FileNameFormat.PngContentType, timeoutCts.Token);

                return true;
            }
            finally
            {
                foreach (var item in streams) await item.DisposeAsync();
                responses.ForEach(x => x.Dispose());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            m_Logger.LogWarning("Multi-image download timed out for {Name}", data.Name);
            return false;
        }
    }
}
