using Grpc.Core;
using Proto = Mehrak.Domain.Protobuf;

namespace Mehrak.ImageProcessor.Shared.Services;

public class GrpcImageProcessorService(
    INsfwClassifier classifier,
    GenshinWeaponImageProcessor weaponImageProcessor,
    PortraitImageMatcher portraitImageMatcher,
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

            // Transport-level guard before any native work: oversized payloads are
            // rejected without decoding. Pixel/dimension budgets are enforced inside
            // the classifier both before (header parse) and after native decoding.
            if (request.ImageData.Length > NsfwClassifier.MaxImageBytes)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Image payload exceeds the size limit."));
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
        catch (RpcException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            // Invalid or over-budget image content: caller error, not a server fault.
            logger.LogWarning(ex, "Rejected image for classification");
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
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

    public override Task<Proto.MatchImageResponse> MatchImage(
        Proto.MatchImageRequest request, ServerCallContext context)
    {
        try
        {
            if (request.ReferenceImage.IsEmpty || request.CandidateImage.IsEmpty)
            {
                return Task.FromResult(new Proto.MatchImageResponse
                {
                    IsMatch = false,
                    Confidence = 0f
                });
            }

            var (isMatch, confidence) = portraitImageMatcher.Match(
                request.ReferenceImage.ToByteArray(), request.CandidateImage.ToByteArray());

            logger.LogDebug("Image match result: IsMatch={IsMatch}, Confidence={Confidence:F4}",
                isMatch, confidence);

            return Task.FromResult(new Proto.MatchImageResponse
            {
                IsMatch = isMatch,
                Confidence = confidence
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error matching image");
            throw new RpcException(new Status(StatusCode.Internal, "Image matching failed."));
        }
    }
}
