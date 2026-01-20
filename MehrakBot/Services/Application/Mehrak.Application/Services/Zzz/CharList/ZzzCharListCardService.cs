using System.Numerics;
using System.Text.Json;
using Mehrak.Application.Models;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Services.Zzz.CharList;

public class ZzzCharListCardService : ICardService<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>, IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly IApplicationMetrics m_Metrics;
    private readonly ILogger<ZzzCharListCardService> m_Logger;

    private static readonly JpegEncoder JpegEncoder = new()
    {
        Interleaved = false,
        Quality = 90,
        ColorType = JpegEncodingColor.Rgb
    };

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_SmallFont;

    private Dictionary<int, Image> m_StarImages = [];

    public ZzzCharListCardService(IImageRepository imageRepository, IApplicationMetrics metrics, ILogger<ZzzCharListCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Metrics = metrics;
        m_Logger = logger;

        FontCollection collection = new();
        var fontFamily = collection.Add("Assets/Fonts/zzz.ttf");

        m_TitleFont = fontFamily.CreateFont(40, FontStyle.Bold);
        m_NormalFont = fontFamily.CreateFont(28, FontStyle.Regular);
        m_SmallFont = fontFamily.CreateFont(20, FontStyle.Regular);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        List<Task<(int x, Image)>> starTasks =
        [
            .. Enumerable.Range(1, 5)
                .Select(async i => (i, await Image.LoadAsync(
                    await m_ImageRepository.DownloadFileToStreamAsync($"zzz_weapon_star_{i}"))))
        ];

        var starImage = await Task.WhenAll(starTasks);
        foreach (var item in starImage)
        {
            item.Item2.Mutate(x => x.Resize(0, 30));
        }

        m_StarImages = starImage.ToDictionary();

        m_Logger.LogInformation(LogMessage.ServiceInitialized, nameof(ZzzCharListCardService));
    }

    public async Task<Stream> GetCardAsync(ICardGenerationContext<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)> context)
    {
        using var cardGenTimer = m_Metrics.ObserveCardGenerationDuration("genshin charlist");
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "CharList", context.UserId);

        var charData = context.Data.Item1.ToList();
        var buddyData = context.Data.Item2.ToList();
        List<IDisposable> disposables = [];

        try
        {
            m_Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
                context.GameProfile.GameUid, charData.Count);

            var avatarImages = await charData
                .OrderByDescending(x => x.Level)
                .ThenByDescending(x => x.Rarity)
                .ThenBy(x => x.Name)
                .ToAsyncEnumerable()
                .Select(async (x, token) =>
                {
                    var image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token), token);
                    using ZzzAvatar avatar = new(x.Id, x.Level, x.Rarity[0], x.Rank, image);
                    return avatar.GetStyledAvatarImage();
                }).ToListAsync();
            disposables.AddRange(avatarImages);

            var buddyImages = await buddyData
                .OrderByDescending(x => x.Level)
                .ThenByDescending(x => x.Star)
                .ThenBy(x => x.Name)
                .ToAsyncEnumerable()
                .Select(async (x, token) =>
                {
                    using var image = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(x!.ToImageName(), token), token);
                    return x.GetStyledBuddyImage(image, m_StarImages[x.Star]);
                })
                .ToListAsync();
            disposables.AddRange(buddyImages);

            var layout = ImageUtility.CalculateSplitGridLayout(avatarImages.Count, buddyImages.Count,
                150, 180, [120, 50, 50, 50], 20, 40);

            using Image<Rgba32> background = new(layout.OutputWidth, layout.OutputHeight + 50);
            background.Mutate(ctx =>
            {
                ctx.Clear(Color.FromRgb(69, 69, 69));

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new Vector2(50, 80),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, $"{context.GameProfile.Nickname}·AR {context.GameProfile.Level}", Color.White);

                ctx.DrawText(new RichTextOptions(m_NormalFont)
                {
                    Origin = new Vector2(50, 110),
                    VerticalAlignment = VerticalAlignment.Bottom
                }, context.GameProfile.GameUid!, Color.White);

                var i = 0;

                while (i < avatarImages.Count)
                {
                    var pos = layout.ImagePositions[i];
                    ctx.DrawImage(avatarImages[i], new Point(pos.X, pos.Y), 1f);
                    i++;
                }

                i = 0;
                while (i < buddyImages.Count)
                {
                    var pos = layout.ImagePositions[avatarImages.Count + i];
                    ctx.DrawImage(buddyImages[i], new Point(pos.X, pos.Y), 1f);
                    i++;
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            m_Logger.LogError(ex, LogMessage.CardGenError, "CharList", context.UserId,
                JsonSerializer.Serialize(context.Data));
            throw new CommandException("Failed to generate CharList card", ex);
        }
        finally
        {
            foreach (var disposable in disposables) disposable.Dispose();
        }
    }
}
