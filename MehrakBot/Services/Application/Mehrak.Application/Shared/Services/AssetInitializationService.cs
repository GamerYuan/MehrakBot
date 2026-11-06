using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;

namespace Mehrak.Application.Shared.Services;

public class AssetInitializationService : IHostedService
{
    private readonly IServiceProvider m_ServiceProvider;

    public AssetInitializationService(IServiceProvider serviceProvider)
    {
        m_ServiceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var imageRepo = m_ServiceProvider.GetRequiredService<IImageRepository>();

        var assetsRoot = Path.Combine(AppContext.BaseDirectory, "Assets");

        var semaphore = new SemaphoreSlim(8);
        var tasks = Directory.EnumerateFiles(assetsRoot, "*.png", SearchOption.AllDirectories)
            .Select(async image =>
                {
                    var relativePath = Path.GetRelativePath(assetsRoot, image);
                    if (Path.GetDirectoryName(relativePath)?.Contains("Test") ?? false) return;

                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var key = GetAssetKey(assetsRoot, image);
                        // Arcana PNGs are processed in place, so replace earlier full-size stored assets.
                        var refreshArcana = key.StartsWith("genshin/arcana_", StringComparison.Ordinal);
                        if (!refreshArcana && await imageRepo.FileExistsAsync(key, cancellationToken)) return;

                        await using var stream = File.OpenRead(image);
                        var uploaded = await imageRepo.UploadFileAsync(key, stream, FileNameFormat.PngContentType, cancellationToken);
                        if (refreshArcana && !uploaded)
                            throw new InvalidOperationException($"Failed to refresh Arcana asset {key}.");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

        await Task.WhenAll(tasks);
    }

    private static string GetAssetKey(string assetsRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(assetsRoot, fullPath);
        var dir = Path.GetDirectoryName(relative)?.Replace('\\', '/');
        var fileName = Path.GetFileName(relative);
        return string.IsNullOrEmpty(dir) ? fileName : $"{dir.ToLowerInvariant()}/{fileName}";
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
