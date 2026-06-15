using Amazon.S3;
using Mehrak.Application.Shared.Abstractions;
using Mehrak.Application.Shared.Renderers;
using Mehrak.Application.Shared.Renderers.Extensions;
using Mehrak.Application.Shared.Utility;
using Mehrak.Domain.Character;
using Mehrak.Domain.Character.Models;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.Domain.Shared.Common;
using Mehrak.Domain.Shared.Enums;
using Mehrak.Domain.User.Abstractions;
using Mehrak.GameApi.Hi3.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Hi3.Character;

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

    private static readonly Color LocalOverlayColor = Color.FromPixel(new Rgba32(47, 87, 126, 196));

    public Hi3CharacterCardService(IImageRepository imageRepository,
        ILogger<Hi3CharacterCardService> logger, IApplicationMetrics metrics)
        : base("Hi3 Character", imageRepository, logger, metrics, LoadFonts("Assets/Fonts/hsr.ttf", 36, 28, smallSize: 18))
    {
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        StaticBackground = await Image.LoadAsync<Rgba32>(await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hi3.BackgroundName), cancellationToken);
        m_StigmataSlot = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hi3.StigmataSlotName), cancellationToken);
        m_StarIcon = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hi3.StarIconName), cancellationToken);
        m_StarUnlit = m_StarIcon.Clone(x => x.Grayscale());

        m_CharacterRankIcons = await new int[] { 1, 2, 3, 4, 5 }
            .ToAsyncEnumerable()
            .Select(async (rank, token) =>
                await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(string.Format(FileNameFormat.Hi3.RankName, rank)), token))
            .ToListAsync(cancellationToken: cancellationToken);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<Hi3CharacterDetail> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var characterInformation = context.Data;

        Image characterImage;
        CharacterPortraitConfig? portraitConfig;
        if (context.PortraitImageStream != null)
        {
            characterImage = await LoadImageFromStreamAsync<Rgba32>(
                context.PortraitImageStream, disposables, cancellationToken);
            portraitConfig = context.PortraitConfig;
        }
        else
        {
            var (loadedImage, _) = await LoadFirstAvailableCostumeImageAsync(characterInformation);
            characterImage = loadedImage;
            disposables.Add(characterImage);

            portraitConfig = context.PortraitConfig;
        }

        characterImage.Mutate(ctx =>
        {
            if (portraitConfig?.TargetScale > 0f)
            {
                var scale = portraitConfig.TargetScale.Value;
                ctx.Resize((int)(ctx.GetCurrentSize().Width * scale), 0, KnownResamplers.Lanczos3);
            }
            else
            {
                ctx.Resize(960, 0, KnownResamplers.Lanczos3);
            }

            if (portraitConfig?.EnableGradientFade == true &&
                (portraitConfig?.GradientFadeStart ?? 0.75f) > 0f)
                ctx.ApplyGradientFade(portraitConfig?.GradientFadeStart ?? 0.75f);
        });

        var weaponImage = await LoadImageFromRepositoryAsync(
            characterInformation.Weapon.ToImageName(), disposables, cancellationToken);

        var stigmataTasks = characterInformation.Stigmatas
            .Select(async (stigmata) =>
            {
                if (stigmata.Id == 0)
                    return (Stigmata: default(Hi3Stigmata?), Image: default(Image));

                var image = await LoadImageFromRepositoryAsync(stigmata.ToImageName(), disposables, cancellationToken);
                return (Stigmata: (Hi3Stigmata?)stigmata, Image: image);
            })
            .ToArray();
        var stigmataImages = await Task.WhenAll(stigmataTasks);

        background.Mutate(ctx =>
        {
            var offsetX = portraitConfig?.OffsetX ?? 0;
            var offsetY = portraitConfig?.OffsetY ?? 0;

            ctx.Paint(canvas =>
            {
                canvas.DrawImage(characterImage, characterImage.Bounds,
                    new RectangleF(350 - characterImage.Width / 2 + offsetX, 425 - characterImage.Height / 2 + offsetY,
                        characterImage.Width, characterImage.Height), KnownResamplers.Bicubic);

                var avatarNameOptions = new RichTextOptions(Fonts.Title)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 600,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };

                canvas.DrawTextWithShadow(characterInformation.Avatar.Name!, avatarNameOptions, Color.White);

                var bounds = TextMeasurer.MeasureBounds(characterInformation.Avatar.Name!,
                    avatarNameOptions);

                canvas.DrawTextWithShadow($"Lv. {characterInformation.Avatar.Level}", Fonts.Normal,
                    new PointF(70, bounds.Bottom + 20), Color.White);

                canvas.DrawTextWithShadow(context.GameProfile.Nickname, Fonts.Normal, new PointF(70, 660), Color.White);
                canvas.DrawTextWithShadow(context.GameProfile.GameUid, Fonts.Small, new PointF(70, 700), Color.White);

                canvas.DrawAttribution(new RichTextOptions(Fonts.Tiny)
                {
                    Origin = new PointF(700, 730),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    TextAlignment = TextAlignment.End,
                }
                );

                var rankIcon = m_CharacterRankIcons[characterInformation.Avatar.Star - 1];
                canvas.DrawImage(rankIcon, rankIcon.Bounds,
                    new RectangleF((int)bounds.Right + 10, (int)bounds.Top + (int)bounds.Height / 2 - 28,
                        rankIcon.Width, rankIcon.Height), KnownResamplers.Bicubic);

                canvas.DrawRoundedRectangleOverlay(600, 700, new PointF(720, 30),
                    new RoundedRectangleOverlayStyle(LocalOverlayColor, CornerRadius: 15));

                canvas.DrawRoundedRectangleOverlay(132, 148, new PointF(750, 50),
                    new RoundedRectangleOverlayStyle(Color.White, CornerRadius: 10));
                canvas.Fill(Brushes.Solid(m_RarityColor[characterInformation.Weapon.Rarity]), new Rectangle(750, 66, 132, 116));
                canvas.DrawImage(weaponImage, weaponImage.Bounds,
                    new RectangleF(750, 66, weaponImage.Width, weaponImage.Height), KnownResamplers.Bicubic);

                var starSize = 15;
                var totalWidth = characterInformation.Weapon.MaxRarity * (starSize + 2) - 2;
                var startX = (128 - totalWidth) / 2 + 745;
                for (var i = characterInformation.Weapon.MaxRarity - 1; i >= 0; i--)
                {
                    var starToDraw = i < characterInformation.Weapon.Rarity ? m_StarIcon : m_StarUnlit;
                    canvas.DrawImage(starToDraw, starToDraw.Bounds,
                        new RectangleF(startX + i * (starSize + 2), 168, starToDraw.Width, starToDraw.Height),
                        KnownResamplers.Bicubic);
                }

                canvas.DrawText(new RichTextOptions(Fonts.Normal)
                {
                    Origin = new PointF(900, 120),
                    VerticalAlignment = VerticalAlignment.Center,
                    WrappingLength = 400
                }, $"{characterInformation.Weapon.Name}\nLv. {characterInformation.Weapon.Level}", Brushes.Solid(Color.White), null);

                var yOffset = 0;
                foreach (var (stigmata, image) in stigmataImages)
                {
                    if (stigmata != null)
                    {
                        DrawStigmataIcon(canvas, new Point(750, 240 + yOffset), image!, stigmata);
                    }
                    else
                    {
                        canvas.DrawImage(m_StigmataSlot, m_StigmataSlot.Bounds,
                            new RectangleF(750, 240 + yOffset, m_StigmataSlot.Width, m_StigmataSlot.Height), KnownResamplers.Bicubic);
                    }

                    canvas.DrawText(new RichTextOptions(Fonts.Normal) { Origin = new PointF(900, 305 + yOffset) },
                        stigmata == null ? "Unequipped" : stigmata.Name,
                        Brushes.Solid(Color.White), null);
                    yOffset += 160;
                }
            });
        });
    }

    private async Task<(Image Image, int CostumeId)> LoadFirstAvailableCostumeImageAsync(Hi3CharacterDetail characterInformation)
    {
        foreach (var costume in characterInformation.Costumes)
        {
            try
            {
                var imageName = costume.ToImageName();
                await using var stream = await ImageRepository.DownloadFileToStreamAsync(imageName);
                if (stream != Stream.Null)
                {
                    return (await Image.LoadAsync(stream), costume.Id);
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

    private void DrawStigmataIcon(DrawingCanvas canvas, Point location, Image stigmataImage, Hi3Stigmata info)
    {
        using var region = canvas.CreateRegion(new Rectangle(location, new Size(132, 148)));
        region.Save(ClipOptions, new RoundedRectanglePolygon(0, 0, 132, 148, 10));
        region.Fill(Brushes.Solid(m_RarityColor[info.Rarity]));
        region.Fill(Brushes.Solid(Color.White), new Rectangle(0, 0, 132, 16));
        region.Fill(Brushes.Solid(Color.White), new Rectangle(0, 132, 132, 16));
        region.Restore();
        region.DrawImage(stigmataImage, stigmataImage.Bounds,
                    new RectangleF(0, 16, stigmataImage.Width, stigmataImage.Height), KnownResamplers.Bicubic);

        var starSize = 15;
        var totalWidth = info.MaxRarity * (starSize + 2) - 2;
        var startX = (128 - totalWidth) / 2 - 5;
        for (var i = info.MaxRarity - 1; i >= 0; i--)
        {
            var starToDraw = i < info.Rarity ? m_StarIcon : m_StarUnlit;
            region.DrawImage(starToDraw, starToDraw.Bounds,
                new RectangleF(startX + i * (starSize + 2), 118, starToDraw.Width, starToDraw.Height),
                KnownResamplers.Bicubic);
        }
    }
}
