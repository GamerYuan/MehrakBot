#region

using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Microsoft.Extensions.Logging;

#endregion

namespace Mehrak.GameApi.Common;

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

    public async Task UpdateImageAsync(IImageData data, IImageProcessor processor)
    {
        if (await m_ImageRepository.FileExistsAsync(data.Name))
        {
            m_Logger.LogInformation("{Name} already exists, skipping download", data.Name);
            return;
        }

        var client = m_HttpClientFactory.CreateClient();

        m_Logger.LogInformation("Downloading {Name} from {Url}", data.Name, data.Url);
        var response = await client.GetAsync(data.Url);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();

        if (processor.ShouldProcess)
        {
            m_Logger.LogInformation("Processing {Name}", data.Name);
            using var processedStream = processor.ProcessImage(stream);
            processedStream.Position = 0;
            await m_ImageRepository.UploadFileAsync(data.Name, processedStream);
        }
        else
        {
            stream.Position = 0;
            await m_ImageRepository.UploadFileAsync(data.Name, stream);
        }
    }

    public async Task UpdateMultiImageAsync(IMultiImageData data, IMultiImageProcessor processor)
    {
        if (await m_ImageRepository.FileExistsAsync(data.Name))
        {
            m_Logger.LogInformation("{Name} already exists, skipping download", data.Name);
            return;
        }

        var client = m_HttpClientFactory.CreateClient();

        m_Logger.LogInformation("Downloading {Name} from supplied Urls: {Url}", data.Name, string.Join(", ", data.Url, data.AdditionalUrls));

        var streams = data.AdditionalUrls.Prepend(data.Url).Where(x => !string.IsNullOrEmpty(x)).ToAsyncEnumerable()
            .SelectAwait(async x => await (await client.GetAsync(x)).Content.ReadAsStreamAsync()).ToEnumerable();

        using var processedStream = processor.ProcessImage(streams);
        processedStream.Position = 0;
        await m_ImageRepository.UploadFileAsync(data.Name, processedStream);

        foreach (var item in streams)
        {
            await item.DisposeAsync();
        }
    }
}
