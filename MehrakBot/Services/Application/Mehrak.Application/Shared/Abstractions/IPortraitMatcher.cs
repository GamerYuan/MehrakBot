namespace Mehrak.Application.Shared.Abstractions;

public interface IPortraitMatcher
{
    Task<(bool IsMatch, float Confidence)> MatchAsync(
        byte[] referenceImage, byte[] candidateImage, CancellationToken cancellationToken = default);
}

public interface IImageFetcher
{
    Task<byte[]?> FetchBytesAsync(string url, CancellationToken cancellationToken = default);
}
