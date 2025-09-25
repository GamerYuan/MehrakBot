#region

using MehrakCore.ApiResponseTypes;
using MehrakCore.Models;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Services.Commands;

public abstract class ImageUpdaterService<T> where T : ICharacterInformation
{
    protected readonly ImageRepository ImageRepository;
    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger<ImageUpdaterService<T>> Logger;

    protected virtual string AvatarString => "{0}";
    protected virtual string SideAvatarString => "{0}";
    protected const int AvatarSize = 150;

    protected ImageUpdaterService(ImageRepository imageRepository, IHttpClientFactory httpClientFactory,
        ILogger<ImageUpdaterService<T>> logger)
    {
        ImageRepository = imageRepository;
        HttpClientFactory = httpClientFactory;
        Logger = logger;
    }

    public abstract Task UpdateDataAsync(T characterInformation, IEnumerable<Dictionary<string, string>> wiki);

    public virtual async ValueTask UpdateAvatarAsync(string avatarId, string avatarUrl)
    {
        try
        {
            if (string.IsNullOrEmpty(avatarId) || string.IsNullOrEmpty(avatarUrl))
            {
                Logger.LogWarning("Avatar ID or URL is null or empty. Skipping update.");
                return;
            }

            string filename = string.Format(AvatarString, avatarId);
            if (await ImageRepository.FileExistsAsync(filename))
            {
                Logger.LogDebug("Avatar image {Filename} already exists. Skipping update.", filename);
                return;
            }

            HttpResponseMessage response = await HttpClientFactory.CreateClient("Default").GetAsync(avatarUrl);
            response.EnsureSuccessStatusCode();
            using Image image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
            image.Mutate(x => x.Resize(AvatarSize, 0, KnownResamplers.Lanczos3));
            using MemoryStream processedImageStream = new();
            await image.SaveAsPngAsync(processedImageStream, new PngEncoder
            {
                BitDepth = PngBitDepth.Bit8,
                ColorType = PngColorType.RgbWithAlpha
            });
            processedImageStream.Position = 0;
            Logger.LogInformation("Uploading avatar image {Filename}", filename);
            await ImageRepository.UploadFileAsync(filename, processedImageStream, "png");
        }
        catch (Exception e)
        {
            throw new CommandException("An error occurred while updating avatar image", e);
        }
    }

    public virtual async ValueTask UpdateSideAvatarAsync(string avatarId, string avatarUrl)
    {
        try
        {
            if (string.IsNullOrEmpty(avatarId) || string.IsNullOrEmpty(avatarUrl))
            {
                Logger.LogWarning("Avatar ID or URL is null or empty. Skipping update.");
                return;
            }

            string filename = string.Format(SideAvatarString, avatarId);
            if (await ImageRepository.FileExistsAsync(filename))
            {
                Logger.LogDebug("Side avatar image {Filename} already exists. Skipping update.", filename);
                return;
            }

            HttpResponseMessage response = await HttpClientFactory.CreateClient("Default").GetAsync(avatarUrl);
            response.EnsureSuccessStatusCode();
            using Image image = await Image.LoadAsync(await response.Content.ReadAsStreamAsync());
            image.Mutate(x => x.Resize(0, AvatarSize, KnownResamplers.Lanczos3));
            using MemoryStream processedImageStream = new();
            await image.SaveAsPngAsync(processedImageStream, new PngEncoder
            {
                BitDepth = PngBitDepth.Bit8,
                ColorType = PngColorType.RgbWithAlpha
            });
            processedImageStream.Position = 0;
            Logger.LogInformation("Uploading side avatar image {Filename}", filename);
            await ImageRepository.UploadFileAsync(filename, processedImageStream, "png");
        }
        catch (Exception e)
        {
            throw new CommandException("An error occurred while updating side avatar image", e);
        }
    }
}
