using Mehrak.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mehrak.Application.Services.Common;

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

        foreach (var image in Directory.EnumerateFiles($"{AppContext.BaseDirectory}Assets", "*.png",
                     SearchOption.AllDirectories))
        {
            if (Path.GetDirectoryName(image)?.Contains("Test") ?? false) continue;
            var fileName = Path.GetFileName(image).Split('.')[0];
            if (await imageRepo.FileExistsAsync(fileName)) continue;

            await using var stream = File.OpenRead(image);
            await imageRepo.UploadFileAsync(fileName, stream);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
