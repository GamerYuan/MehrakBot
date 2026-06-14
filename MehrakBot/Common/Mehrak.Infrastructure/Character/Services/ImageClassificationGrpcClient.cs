using Mehrak.Domain.Shared.Services;
using Microsoft.Extensions.Logging;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Infrastructure.Character.Services;

public class ImageClassificationGrpcClient : IImageClassificationService
{
    private readonly Proto.ImageProcessorService.ImageProcessorServiceClient m_Client;
    private readonly ILogger<ImageClassificationGrpcClient> m_Logger;

    public ImageClassificationGrpcClient(
        Proto.ImageProcessorService.ImageProcessorServiceClient client,
        ILogger<ImageClassificationGrpcClient> logger)
    {
        m_Client = client;
        m_Logger = logger;
    }

    public async Task<ImageClassificationResult> ClassifyAsync(Stream imageStream, CancellationToken ct = default)
    {
        try
        {
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;

            var request = new Proto.ClassifyRequest
            {
                ImageData = Google.Protobuf.ByteString.FromStream(memoryStream)
            };

            var response = await m_Client.ClassifyImageAsync(request, cancellationToken: ct);

            return new ImageClassificationResult(
                response.IsNsfw,
                response.NsfwConfidence,
                response.SfwConfidence);
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to classify image via gRPC");
            throw;
        }
    }
}
