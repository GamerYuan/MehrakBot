using Grpc.Core;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.ImageProcessor.Shared.Services;

public class GrpcImageProcessorService(
    INsfwClassifier classifier,
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
}
