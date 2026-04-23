using System.Globalization;
using System.Numerics;
using Mehrak.Application.Models;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Services.Zzz.CharList;

public class ZzzCharListCardService : CardServiceBase<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)>
{
    private Dictionary<int, Image> m_StarImages = [];

    private static readonly Dictionary<string, Color> ElementForeground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Physical", Color.FromRgb(255, 226, 0) },
        { "Fire", Color.FromRgb(254, 120, 26) },
        { "Ice", Color.FromRgb(126, 233, 232) },
        { "Electric", Color.FromRgb(37, 218, 250) },
        { "Ether", Color.FromRgb(252, 23, 40) },
    };

    private static readonly Dictionary<string, Color> ElementBackground = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Physical", Color.FromRgba(226, 137, 3, 128) },
        { "Fire", Color.FromRgba(240, 25, 2, 128) },
        { "Ice", Color.FromRgba(11, 207, 213, 128) },
        { "Electric", Color.FromRgba(2, 121, 254, 128) },
        { "Ether", Color.FromRgba(132, 99, 240, 128) },
    };

    private static readonly char[] RarityOrder = ['S', 'A'];

    private static readonly Color GoldBackgroundColor = Color.FromRgb(183, 125, 76);
    private static readonly Color PurpleBackgroundColor = Color.FromRgb(132, 104, 173);
    private static readonly Color PurpleForegroundColor = Color.FromRgb(204, 173, 255);

    private static readonly TextInfo TextInfo = new CultureInfo("en-US", false).TextInfo;

    public ZzzCharListCardService(IImageRepository imageRepository,
        ILogger<ZzzCharListCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Zzz CharList",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/zzz.ttf", 40f, 28f, null, null))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        List<Task<(int x, Image)>> starTasks =
        [
            .. Enumerable.Range(1, 5)
                .Select(async i => {
                    await using var stream = await ImageRepository.DownloadFileToStreamAsync($"zzz_weapon_star_{i}", cancellationToken);
                    return (i, await Image.LoadAsync(stream, cancellationToken));
                })
        ];

        var starImage = await Task.WhenAll(starTasks);
        foreach (var item in starImage)
        {
            item.Item2.Mutate(x => x.Resize(0, 30));
        }

        m_StarImages = starImage.ToDictionary();
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return new Image<Rgba32>(1, 1);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<(IEnumerable<ZzzBasicAvatarData>, IEnumerable<ZzzBuddyData>)> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var charData = context.Data.Item1.ToList();
        var buddyData = context.Data.Item2.ToList();

        Logger.LogInformation("Generating character list card for user {UserId} with {CharCount} characters",
            context.UserId, charData.Count);

        var avatarImages = await charData
            .OrderByDescending(x => x.Level)
            .ThenByDescending(x => x.Rarity)
            .ThenBy(x => x.Name)
            .ToAsyncEnumerable()
            .Select(async (x, token) =>
            {
                var image = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(x.ToImageName(), token), token);
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
                using var image = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(x!.ToImageName(), token), token);
                return x.GetStyledBuddyImage(image, m_StarImages[x.Star]);
            })
            .ToListAsync();
        disposables.AddRange(buddyImages);

        var layout = ImageUtility.CalculateSplitGridLayout(avatarImages.Count, buddyImages.Count,
            150, 180, [120, 50, 50, 50], 20, 80);

        var charCountByElem = charData
            .GroupBy(x => x.ElementType)
            .OrderBy(x => x.Key)
            .Select(x => new { Element = ToTitleCase(StatUtils.GetElementNameFromId(x.Key, 0)), Count = x.Count() })
            .ToList();

        var charCountByProfession = charData
            .GroupBy(x => x.AvatarProfession)
            .OrderBy(x => x.Key)
            .Select(x => new { Profession = StatUtils.GetProfessionNameFromId(x.Key), Count = x.Count() })
            .ToList();

        var charCountByRarity = charData
            .GroupBy(x => x.Rarity.ToUpperInvariant())
            .OrderBy(x => RarityOrder.IndexOf(x.Key[0]))
            .Select(x => new { Rarity = $"{x.Key}-Rank", Count = x.Count() })
            .ToList();

        background.Mutate(ctx => ctx.Resize(layout.OutputWidth, layout.OutputHeight + 180));

        background.Mutate(ctx =>
        {
            ctx.Clear(Color.FromRgb(69, 69, 69));

            ctx.DrawText(new RichTextOptions(Fonts.Title)
            {
                Origin = new Vector2(50, 80),
                VerticalAlignment = VerticalAlignment.Bottom
            }, $"{context.GameProfile.Nickname}·IK {context.GameProfile.Level}", Color.White);

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
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

            var yOffset = layout.OutputHeight - 25;
            var xOffset = 50;

            foreach (var entry in charCountByRarity)
            {
                var countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                    new TextOptions(Fonts.Normal));
                var elemSize =
                    TextMeasurer.MeasureSize(entry.Rarity, new TextOptions(Fonts.Normal));
                FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                    countSize.Height + elemSize.Height);
                EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                ctx.DrawRoundedRectangleOverlay((int)size.Width + 50, 50, new PointF(xOffset, yOffset),
                    new RoundedRectangleOverlayStyle(
                        entry.Rarity == "S-Rank" ? GoldBackgroundColor.WithAlpha(128) : PurpleBackgroundColor.WithAlpha(128),
                        CornerRadius: 10));
                ctx.Fill(entry.Rarity == "S-Rank" ? Color.Gold : PurpleForegroundColor, foreground);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 40, yOffset + 32),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Rarity, Color.White);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 32),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Count.ToString(), Color.White);
                xOffset += (int)size.Width + 70;
            }

            yOffset += 60;
            xOffset = 50;

            foreach (var entry in charCountByElem)
            {
                var countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                    new TextOptions(Fonts.Normal));
                var elemSize = TextMeasurer.MeasureSize(entry.Element, new TextOptions(Fonts.Normal));
                FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                    countSize.Height + elemSize.Height);
                EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                ctx.DrawRoundedRectangleOverlay((int)size.Width + 50, 50, new PointF(xOffset, yOffset),
                    new RoundedRectangleOverlayStyle(ElementBackground[entry.Element], CornerRadius: 10));
                ctx.Fill(ElementForeground[entry.Element], foreground);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 40, yOffset + 32),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Element, Color.White);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 32),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Count.ToString(), Color.White);
                xOffset += (int)size.Width + 70;
            }

            yOffset += 60;
            xOffset = 50;

            foreach (var entry in charCountByProfession)
            {
                var countSize = TextMeasurer.MeasureSize(entry.Count.ToString(),
                    new TextOptions(Fonts.Normal));
                var elemSize = TextMeasurer.MeasureSize(entry.Profession, new TextOptions(Fonts.Normal));
                FontRectangle size = new(0, 0, countSize.Width + elemSize.Width + 20,
                    countSize.Height + elemSize.Height);
                EllipsePolygon foreground = new(new PointF(xOffset + 20, yOffset + 25), 10);
                ctx.DrawRoundedRectangleOverlay((int)size.Width + 50, 50, new PointF(xOffset, yOffset),
                    new RoundedRectangleOverlayStyle(Color.FromRgb(24, 24, 24), CornerRadius: 10));
                ctx.Fill(Color.White, foreground);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 40, yOffset + 32),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Profession, Color.White);
                ctx.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new Vector2(xOffset + 35 + size.Width, yOffset + 32),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, entry.Count.ToString(), Color.White);
                xOffset += (int)size.Width + 70;
            }
        });

        Logger.LogInformation("Completed character list card for user {UserId} with {CharCount} characters",
            context.UserId, charData.Count);
    }

    private static string ToTitleCase(string str)
    {
        return TextInfo.ToTitleCase(str.ToLowerInvariant());
    }
}
