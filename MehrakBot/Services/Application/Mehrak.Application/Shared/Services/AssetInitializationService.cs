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

        var tasks = Directory.EnumerateFiles(assetsRoot, "*.png", SearchOption.AllDirectories)
            .Select(async image =>
                {
                    if (Path.GetDirectoryName(image)?.Contains("Test") ?? false) return;

                    var key = GetAssetKey(assetsRoot, image);
                    if (await imageRepo.FileExistsAsync(key, cancellationToken)) return;

                    await using var stream = File.OpenRead(image);
                    await imageRepo.UploadFileAsync(key, stream, FileNameFormat.PngContentType, cancellationToken);
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
