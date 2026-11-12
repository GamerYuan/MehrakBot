namespace Mehrak.Domain.Shared.Storage;

public interface IAssetPrefixCleanupService
{
    Task<AssetPrefixCleanupResult> CleanupByPrefixAsync(
        AssetPrefixCleanupRequest request,
        CancellationToken cancellationToken = default);
}
