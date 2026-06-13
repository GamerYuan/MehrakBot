namespace Mehrak.Domain.Shared.Services;

public interface IImageClassificationService
{
    Task<ImageClassificationResult> ClassifyAsync(Stream imageStream, CancellationToken ct = default);
}

public record ImageClassificationResult(bool IsNsfw, float NsfwConfidence, float SfwConfidence);
