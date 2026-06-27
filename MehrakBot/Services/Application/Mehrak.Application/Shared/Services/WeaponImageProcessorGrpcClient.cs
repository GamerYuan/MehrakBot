using Mehrak.Domain.Image.Abstractions;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.Application.Shared.Services;

internal class WeaponImageProcessorGrpcClient : IMultiImageProcessor
{
    private readonly Proto.ImageProcessorService.ImageProcessorServiceClient m_Client;
    private readonly ILogger<WeaponImageProcessorGrpcClient> m_Logger;

    public bool ShouldProcess => true;

    public WeaponImageProcessorGrpcClient(
        Proto.ImageProcessorService.ImageProcessorServiceClient client,
        ILogger<WeaponImageProcessorGrpcClient> logger)
    {
        m_Client = client;
        m_Logger = logger;
    }

    public Stream ProcessImage(IEnumerable<Stream> images)
    {
        try
        {
            var request = new Proto.ProcessWeaponImageRequest();
            foreach (var image in images)
            {
                using var ms = new MemoryStream();
                image.CopyTo(ms);
                ms.Position = 0;
                request.Images.Add(Google.Protobuf.ByteString.FromStream(ms));
            }

            var response = m_Client.ProcessWeaponImage(request);

            if (response.ProcessedImage.IsEmpty)
            {
                return Stream.Null;
            }

            return new MemoryStream(response.ProcessedImage.ToByteArray());
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, "Failed to process weapon image via gRPC");
            throw;
        }
    }
}
