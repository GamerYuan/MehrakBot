using System.Diagnostics;
using System.Text.Json;
using Mehrak.Application.Services.Hi3.Types;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Services.Abstractions;
using Mehrak.GameApi.Hi3.Types;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mehrak.Application.Services.Hi3.Character;

internal class Hi3CharacterCardService :
    ICardService<Hi3CardGenerationContext<Hi3CharacterDetail>, Hi3CharacterDetail>,
    IAsyncInitializable
{
    private readonly IImageRepository m_ImageRepository;
    private readonly ILogger<Hi3CharacterCardService> m_Logger;

    private readonly Font m_TitleFont;
    private readonly Font m_NormalFont;
    private readonly Font m_MediumFont;

    private readonly JpegEncoder m_JpegEncoder;

    private readonly Dictionary<int, Color> m_RarityColor = new()
    {
        { 1, Color.ParseHex("1d9669") },
        { 2, Color.ParseHex("4faee0") },
        { 3, Color.ParseHex("4faee0") },
        { 4, Color.ParseHex("8745b1") },
        { 5, Color.ParseHex("8745b1") },
        { 6, Color.ParseHex("f0b74f") }
    };

    private Image m_Background;
    private Image m_StigmataSlot;
    private Image m_StarIcon;
    private Image m_StarUnlit;
    private List<Image> m_CharacterRankIcons;

    private static readonly Color OverlayColor = Color.FromRgba(47, 87, 126, 196);

    public Hi3CharacterCardService(IImageRepository imageRepository,
        ILogger<Hi3CharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontFamily fontFamily = new FontCollection().Add("Assets/Fonts/hsr.ttf");

        m_TitleFont = fontFamily.CreateFont(36);
        m_NormalFont = fontFamily.CreateFont(28);
        m_MediumFont = fontFamily.CreateFont(18);

        m_JpegEncoder = new JpegEncoder
        {
            Quality = 90,
            Interleaved = false
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        m_Background = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hi3_bg"), cancellationToken);
        m_StigmataSlot = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hi3_stigmata_slot"), cancellationToken);
        m_StarIcon = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync("hi3_star_icon"), cancellationToken);
        m_StarUnlit = m_StarIcon.Clone(x => x.Grayscale());

        m_CharacterRankIcons = await new int[] { 1, 2, 3, 4, 5 }
            .ToAsyncEnumerable()
            .Select(async (rank, token) =>
                await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync($"hi3_rank_{rank}"), token))
            .ToListAsync(cancellationToken: cancellationToken);
    }

    public async Task<Stream> GetCardAsync(Hi3CardGenerationContext<Hi3CharacterDetail> context)
    {
        m_Logger.LogInformation(LogMessage.CardGenStartInfo, "Character", context.UserId);
        Stopwatch stopwatch = Stopwatch.StartNew();

        Hi3CharacterDetail characterInformation = context.Data;

        List<IDisposable> disposableResources = [];

        try
        {
            var characterImage = await Image.LoadAsync(
                await m_ImageRepository.DownloadFileToStreamAsync(characterInformation.Costumes[0].ToImageName()));
            disposableResources.Add(characterImage);

            var weaponImage = await Image.LoadAsync(
                await m_ImageRepository.DownloadFileToStreamAsync(characterInformation.Weapon.ToImageName()));
            disposableResources.Add(weaponImage);

            var stigmataImages = await characterInformation.Stigmatas
                .ToAsyncEnumerable()
                .ToDictionaryAsync(
                    async (stigmata, token) => await Task.FromResult(stigmata),
                    async (stigmata, token) =>
                    {
                        if (stigmata.Id == 0) return m_StigmataSlot.Clone(ctx => { });

                        var img = await Image.LoadAsync(
                            await m_ImageRepository.DownloadFileToStreamAsync(stigmata.ToImageName()), token);
                        var stigmataIcon = GetStigmataIcon(img, stigmata);
                        return stigmataIcon;
                    });
            disposableResources.AddRange(stigmataImages.Values);

            var image = m_Background.CloneAs<Rgba32>();
            disposableResources.Add(image);

            image.Mutate(ctx =>
            {
                ctx.DrawImage(characterImage,
                    new Point(350 - characterImage.Width / 2, 425 - characterImage.Height / 2), 1f);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(73, 53),
                    WrappingLength = 600,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, characterInformation.Avatar.Name!, Color.Black);
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 600,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, characterInformation.Avatar.Name!, Color.White);

                FontRectangle bounds = TextMeasurer.MeasureBounds(characterInformation.Avatar.Name!,
                    new RichTextOptions(m_TitleFont)
                    {
                        Origin = new PointF(70, 50),
                        WrappingLength = 700,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    });

                ctx.DrawText($"Lv. {characterInformation.Avatar.Level}", m_NormalFont, Color.Black,
                    new PointF(73, bounds.Bottom + 23));
                ctx.DrawText($"Lv. {characterInformation.Avatar.Level}", m_NormalFont, Color.White,
                    new PointF(70, bounds.Bottom + 20));
                ctx.DrawText(context.GameProfile.GameUid, m_MediumFont, Color.White, new PointF(70, 700));

                ctx.DrawImage(m_CharacterRankIcons[characterInformation.Avatar.Star - 1],
                    new Point((int)bounds.Right + 10, (int)bounds.Top + (int)bounds.Height / 2 - 28), 1f);

                ctx.Fill(OverlayColor, ImageUtility.CreateRoundedRectanglePath(600, 700, 15).Translate(720, 30));

                ctx.Fill(Color.White, ImageUtility.CreateRoundedRectanglePath(132, 148, 10).Translate(750, 50));
                ctx.Fill(m_RarityColor[characterInformation.Weapon.Rarity], new RectangleF(750, 66, 132, 116));
                ctx.DrawImage(weaponImage, new Point(750, 66), 1f);

                int starSize = 15;
                int totalWidth = characterInformation.Weapon.MaxRarity * (starSize + 2) - 2;
                int startX = (128 - totalWidth) / 2 + 745;
                for (int i = characterInformation.Weapon.MaxRarity - 1; i >= 0; i--)
                {
                    Image starToDraw = i < characterInformation.Weapon.Rarity ? m_StarIcon : m_StarUnlit;
                    ctx.DrawImage(starToDraw, new Point(startX + i * (starSize + 2), 168), 1f);
                }

                ctx.DrawText(characterInformation.Weapon.Name, m_NormalFont, Color.White, new PointF(900, 90));
                ctx.DrawText($"Lv. {characterInformation.Weapon.Level}", m_NormalFont, Color.White, new PointF(900, 120));

                int yOffset = 0;
                foreach (var entry in stigmataImages)
                {
                    ctx.DrawImage(entry.Value, new Point(750, 240 + yOffset), 1f);
                    ctx.DrawText(entry.Key.Id == 0 ? "Unequipped" : entry.Key.Name,
                        m_NormalFont, Color.White, new PointF(900, 305 + yOffset));
                    yOffset += 160;
                }
            });

            Stream stream = new MemoryStream();
            await image.SaveAsJpegAsync(stream, m_JpegEncoder);
            stream.Position = 0;

            m_Logger.LogInformation(LogMessage.CardGenSuccess, "Character", context.UserId,
                stopwatch.ElapsedMilliseconds);

            return stream;

        }
        catch (Exception e)
        {
            m_Logger.LogError(e, LogMessage.CardGenError, "Character", context.UserId,
                JsonSerializer.Serialize(context.Data));
            throw new CommandException("Failed to generate Character card", e);
        }
        finally
        {
            disposableResources.ForEach(x => x.Dispose());
        }
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

            int starSize = 15;
            int totalWidth = info.MaxRarity * (starSize + 2) - 2;
            int startX = (128 - totalWidth) / 2 - 5;
            for (int i = info.MaxRarity - 1; i >= 0; i--)
            {
                Image starToDraw = i < info.Rarity ? m_StarIcon : m_StarUnlit;
                ctx.DrawImage(starToDraw, new Point(startX + i * (starSize + 2), 118), 1f);
            }

            ctx.ApplyRoundedCorners(10);
        });
        stigmataImage.Dispose();
        return stigmataIcon;
    }
}
