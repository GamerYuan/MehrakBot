namespace Mehrak.Domain.Shared.Storage;

public interface IAssetSuffixCleanupService
{
    Task<AssetSuffixCleanupResult> CleanupBySuffixAsync(
        AssetSuffixCleanupRequest request,
        CancellationToken cancellationToken = default);
}
