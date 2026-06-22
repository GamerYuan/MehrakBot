using Grpc.Core;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.ImageProcessor.Shared.Services;

public class GrpcImageProcessorService(
    INsfwClassifier classifier,
    GenshinWeaponImageProcessor weaponImageProcessor,
    ILogger<GrpcImageProcessorService> logger) : Proto.ImageProcessorService.ImageProcessorServiceBase
{
    public override Task<Proto.ClassifyResponse> ClassifyImage(Proto.ClassifyRequest request, ServerCallContext context)
    {
        try
        {
            if (request.ImageData.IsEmpty)
            {
                return Task.FromResult(new Proto.ClassifyResponse
                {
                    IsNsfw = false,
                    NsfwConfidence = 0f,
                    SfwConfidence = 0f
                });
            }

            var result = classifier.Classify(request.ImageData.ToByteArray());

            logger.LogDebug("Image classified: IsNsfw={IsNsfw}, NSFW={NsfwConfidence:F4}, SFW={SfwConfidence:F4}",
                result.IsNsfw, result.NsfwConfidence, result.SfwConfidence);

            return Task.FromResult(new Proto.ClassifyResponse
            {
                IsNsfw = result.IsNsfw,
                NsfwConfidence = result.NsfwConfidence,
                SfwConfidence = result.SfwConfidence
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error classifying image");
            throw new RpcException(new Status(StatusCode.Internal, "Image classification failed."));
        }
    }

    public override Task<Proto.ProcessWeaponImageResponse> ProcessWeaponImage(
        Proto.ProcessWeaponImageRequest request, ServerCallContext context)
    {
        var streams = request.Images.Select(bytes => new MemoryStream(bytes.ToByteArray()) as Stream).ToList();
        try
        {
            using var resultStream = weaponImageProcessor.ProcessImage(streams);

            if (resultStream == Stream.Null || resultStream.Length == 0)
            {
                return Task.FromResult(new Proto.ProcessWeaponImageResponse());
            }

            using var ms = new MemoryStream();
            resultStream.CopyTo(ms);
            ms.Position = 0;

            return Task.FromResult(new Proto.ProcessWeaponImageResponse
            {
                ProcessedImage = Google.Protobuf.ByteString.FromStream(ms)
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing weapon image");
            throw new RpcException(new Status(StatusCode.Internal, "Weapon image processing failed."));
        }
        finally
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }
        }
    }
}
