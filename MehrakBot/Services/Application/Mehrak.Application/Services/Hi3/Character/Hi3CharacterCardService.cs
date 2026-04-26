using Amazon.S3;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hi3.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Services.Hi3.Character;

internal class Hi3CharacterCardService : CardServiceBase<Hi3CharacterDetail>
{
    private readonly Dictionary<int, Color> m_RarityColor = new()
    {
        { 1, Color.ParseHex("1d9669") },
        { 2, Color.ParseHex("4faee0") },
        { 3, Color.ParseHex("4faee0") },
        { 4, Color.ParseHex("8745b1") },
        { 5, Color.ParseHex("8745b1") },
        { 6, Color.ParseHex("f0b74f") }
    };

    private Image m_StigmataSlot = null!;
    private Image m_StarIcon = null!;
    private Image m_StarUnlit = null!;
    private List<Image> m_CharacterRankIcons = [];

    private static readonly Color LocalOverlayColor = Color.FromRgba(47, 87, 126, 196);

    public Hi3CharacterCardService(IImageRepository imageRepository,
        ILogger<Hi3CharacterCardService> logger, IApplicationMetrics metrics)
        : base("Hi3 Character", imageRepository, logger, metrics, LoadFonts("Assets/Fonts/hsr.ttf", 36, 28, smallSize: 18))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        StaticBackground = await Image.LoadAsync<Rgba32>(await ImageRepository.DownloadFileToStreamAsync("hi3_bg"), cancellationToken);
        m_StigmataSlot = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync("hi3_stigmata_slot"), cancellationToken);
        m_StarIcon = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync("hi3_star_icon"), cancellationToken);
        m_StarUnlit = m_StarIcon.Clone(x => x.Grayscale());

        m_CharacterRankIcons = await new int[] { 1, 2, 3, 4, 5 }
            .ToAsyncEnumerable()
            .Select(async (rank, token) =>
                await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync($"hi3_rank_{rank}"), token))
            .ToListAsync(cancellationToken: cancellationToken);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<Hi3CharacterDetail> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var characterInformation = context.Data;

        var characterImage = await LoadFirstAvailableCostumeImageAsync(characterInformation);
        disposables.Add(characterImage);

        var weaponImage = await LoadImageFromRepositoryAsync(
            characterInformation.Weapon.ToImageName(), disposables, cancellationToken);

        var stigmataImages = await characterInformation.Stigmatas
            .ToAsyncEnumerable()
            .ToDictionaryAsync(
                (stigmata, token) => ValueTask.FromResult(stigmata),
                async (stigmata, token) =>
                {
                    if (stigmata.Id == 0)
                    {
                        var empty = m_StigmataSlot.Clone(ctx => { });
                        disposables.Add(empty);
                        return empty;
                    }

                    await using var stream = await ImageRepository.DownloadFileToStreamAsync(stigmata.ToImageName(), token);
                    using var img = await Image.LoadAsync(stream, token);
                    var stigmataIcon = GetStigmataIcon(img, stigmata);
                    disposables.Add(stigmataIcon);
                    return stigmataIcon;
                }, cancellationToken: cancellationToken);

        background.Mutate(ctx =>
        {
            ctx.DrawImage(characterImage,
                new Point(350 - characterImage.Width / 2, 425 - characterImage.Height / 2), 1f);

            var avatarNameOptions = new RichTextOptions(Fonts.Title)
            {
                Origin = new PointF(70, 50),
                WrappingLength = 600,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            ctx.DrawTextWithShadow(characterInformation.Avatar.Name!, avatarNameOptions, Color.White);

            var bounds = TextMeasurer.MeasureBounds(characterInformation.Avatar.Name!,
                avatarNameOptions);

            ctx.DrawTextWithShadow($"Lv. {characterInformation.Avatar.Level}", Fonts.Normal,
                new PointF(70, bounds.Bottom + 20), Color.White);
            ctx.DrawText(context.GameProfile.GameUid, Fonts.Small, Color.White, new PointF(70, 700));

            ctx.DrawImage(m_CharacterRankIcons[characterInformation.Avatar.Star - 1],
                new Point((int)bounds.Right + 10, (int)bounds.Top + (int)bounds.Height / 2 - 28), 1f);

            ctx.DrawRoundedRectangleOverlay(600, 700, new PointF(720, 30),
                new RoundedRectangleOverlayStyle(LocalOverlayColor, CornerRadius: 15));

            ctx.DrawRoundedRectangleOverlay(132, 148, new PointF(750, 50),
                new RoundedRectangleOverlayStyle(Color.White, CornerRadius: 10));
            ctx.Fill(m_RarityColor[characterInformation.Weapon.Rarity], new RectangleF(750, 66, 132, 116));
            ctx.DrawImage(weaponImage, new Point(750, 66), 1f);

            var starSize = 15;
            var totalWidth = characterInformation.Weapon.MaxRarity * (starSize + 2) - 2;
            var startX = (128 - totalWidth) / 2 + 745;
            for (var i = characterInformation.Weapon.MaxRarity - 1; i >= 0; i--)
            {
                var starToDraw = i < characterInformation.Weapon.Rarity ? m_StarIcon : m_StarUnlit;
                ctx.DrawImage(starToDraw, new Point(startX + i * (starSize + 2), 168), 1f);
            }

            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(900, 120),
                VerticalAlignment = VerticalAlignment.Center,
                WrappingLength = 400
            }, $"{characterInformation.Weapon.Name}\nLv. {characterInformation.Weapon.Level}", Color.White);

            var yOffset = 0;
            foreach (var entry in stigmataImages)
            {
                ctx.DrawImage(entry.Value, new Point(750, 240 + yOffset), 1f);
                ctx.DrawText(entry.Key.Id == 0 ? "Unequipped" : entry.Key.Name,
                    Fonts.Normal, Color.White, new PointF(900, 305 + yOffset));
                yOffset += 160;
            }
        });
    }

    private async Task<Image> LoadFirstAvailableCostumeImageAsync(Hi3CharacterDetail characterInformation)
    {
        foreach (var costume in characterInformation.Costumes)
        {
            try
            {
                var imageName = costume.ToImageName();
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(imageName);
                if (stream != Stream.Null)
                {
                    return await Image.LoadAsync(stream);
                }

            }
            catch (AmazonS3Exception e)
            {
                Logger.LogWarning(e, "Failed to load costume image for costume {CostumeId} of character {CharacterId}",
                    costume.Id, characterInformation.Avatar.Id);
            }
        }


        throw new CommandException("No splash art image found for character");
    }

    private Image GetStigmataIcon(Image stigmataImage, Hi3Stigmata info)
    {
        Image stigmataIcon = new Image<Rgba32>(132, 148);
        stigmataIcon.Mutate(ctx =>
        {
            ctx.Fill(m_RarityColor[info.Rarity]);

            ctx.DrawImage(stigmataImage, new Point(0, 16), 1f);

            ctx.Fill(Color.White, new RectangleF(0, 0, 132, 16));
            ctx.Fill(Color.White, new RectangleF(0, 132, 132, 16));

            var starSize = 15;
            var totalWidth = info.MaxRarity * (starSize + 2) - 2;
            var startX = (128 - totalWidth) / 2 - 5;
            for (var i = info.MaxRarity - 1; i >= 0; i--)
            {
                var starToDraw = i < info.Rarity ? m_StarIcon : m_StarUnlit;
                ctx.DrawImage(starToDraw, new Point(startX + i * (starSize + 2), 118), 1f);
            }

            ctx.ApplyRoundedCorners(10);
        });
        stigmataImage.Dispose();
        return stigmataIcon;
    }
}
