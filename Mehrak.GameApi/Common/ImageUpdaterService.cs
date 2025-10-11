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
            return;
        }

        var client = m_HttpClientFactory.CreateClient();

        var response = await client.GetAsync(data.Url);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();

        if (processor.ShouldProcess())
        {
            await using var processedStream = processor.ProcessImage(stream);
            processedStream.Position = 0;
            await m_ImageRepository.UploadFileAsync(data.Name, processedStream);
        }
        else
        {
            stream.Position = 0;
            await m_ImageRepository.UploadFileAsync(data.Name, stream);
        }
    }
}
