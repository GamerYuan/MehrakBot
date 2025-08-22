using MehrakCore.ApiResponseTypes.Zzz;
using MehrakCore.Constants;
using MehrakCore.Models;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using System.Text.Json;

namespace MehrakCore.Services.Commands.Zzz.Character;

internal class ZzzCharacterCardService : ICharacterCardService<ZzzFullAvatarData>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<ZzzCharacterCardService> m_Logger;

    private readonly Dictionary<string, Image> m_StatImages;
    private readonly Dictionary<int, Image> m_SkillImages;
    private readonly Dictionary<int, Image> m_SpecialAttributeImages;
    private readonly Dictionary<int, Image> m_ProfessionImages;
    private readonly Dictionary<char, Image> m_AgentRankImages;
    private readonly Dictionary<char, Image> m_RarityImages;

    private readonly Font m_SmallFont;
    private readonly Font m_NormalFont;
    private readonly Font m_MediumFont;
    private readonly Font m_TitleFont;
    private readonly Font m_ExtraLargeFont;

    private readonly JpegEncoder m_JpegEncoder;

    private static readonly string BasePath = FileNameFormat.ZzzFileName;

    private readonly Image m_WeaponTemplate = null!;
    private readonly Image<Rgba32> m_DiskTemplate = null!;

    private static readonly Color BackgroundColor = Color.FromRgb(69, 69, 69);
    private static readonly Color OverlayColor = Color.FromRgb(36, 36, 36);

    public ZzzCharacterCardService(ImageRepository imageRepository, ILogger<ZzzCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;

        FontFamily fontFamily = new FontCollection().Add("Assets/Fonts/zzz.ttf");

        m_ExtraLargeFont = new FontCollection().AddSystemFonts().Get("Impact").CreateFont(400, FontStyle.Italic);
        m_TitleFont = fontFamily.CreateFont(64);
        m_NormalFont = fontFamily.CreateFont(40);
        m_MediumFont = fontFamily.CreateFont(36);
        m_SmallFont = fontFamily.CreateFont(28);

        m_JpegEncoder = new JpegEncoder
        {
            Quality = 90,
            Interleaved = false
        };

        List<string> files = imageRepository.ListFilesAsync("zzz_stats").Result;
        List<Task<(string x, Image)>> tasks = [.. files.Select(async file =>
            (file.Replace("zzz_stats_", "").TrimStart(),
            await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(file))))];

        int[] skillIds = [0, 1, 2, 3, 5, 6];
        List<Task<(int x, Image)>> skillTasks = [.. skillIds.Select(async id =>
            (id, await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format(FileNameFormat.ZzzSkillName, id)))))];

        List<Task<(int x, Image)>> specialAttributeTasks = [.. Enumerable.Range(1, 2).Select(async i =>
            (i, await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format("zzz_attribute_{0}", StatUtils.GetSpecialAttributeName(i))))))];

        List<Task<(int x, Image)>> professionTasks = [.. Enumerable.Range(1, 6).Select(async i =>
            (i, await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format(FileNameFormat.ZzzProfessionName, i)))))];

        char[] itemRarity = ['s', 'a', 'b'];
        List<Task<(char x, Image)>> rarityTasks = [.. itemRarity.Select(async i =>
            (i, await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format("zzz_rarity_{0}", i)))))];

        m_StatImages = Task.WhenAll(tasks).Result.ToDictionary(StringComparer.OrdinalIgnoreCase);
        m_SkillImages = Task.WhenAll(skillTasks).Result.ToDictionary();
        m_SpecialAttributeImages = Task.WhenAll(specialAttributeTasks).Result.ToDictionary();
        m_ProfessionImages = Task.WhenAll(professionTasks).Result.ToDictionary();
        m_RarityImages = Task.WhenAll(rarityTasks).Result.ToDictionary(CaseInsensitiveCharComparer.Instance);

        m_WeaponTemplate = new Image<Rgba32>(100, 100);

        m_DiskTemplate = new Image<Rgba32>(750, 150);
        m_DiskTemplate.Mutate(x =>
        {
            x.Fill(new Rgba32(255, 255, 255, 0.1f));
            x.ApplyRoundedCorners(30);
        });
    }

    public async Task<Stream> GenerateCharacterCardAsync(ZzzFullAvatarData characterInformation, string gameUid)
    {
        List<IDisposable> disposables = [];

        try
        {
            m_Logger.LogInformation("Generating character card for UID: {GameUid}, Character: {CharacterName}",
                gameUid, characterInformation.AvatarList[0].Name);

            ZzzAvatarData character = characterInformation.AvatarList[0];

            Image<Rgba32> background = new(3000, 1200);

            Task<Image> portraitTask = Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                string.Format(BasePath, character.Id)));
            Task<Image> weaponTask = character.Weapon == null
                ? Task.FromResult(m_WeaponTemplate.Clone(ctx => { }))
                : Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
                    string.Format(BasePath, character.Weapon?.Id)));
            List<Image> diskImage = await Enumerable.Range(1, 6).ToAsyncEnumerable().SelectAwait(async i =>
            {
                DiskDrive? disk = character.Equip.FirstOrDefault(x => x.EquipmentType == i);
                if (disk == null)
                {
                    return CreateDiskTemplateImageAsync();
                }
                else
                {
                    return await CreateDiskImageAsync(disk);
                }
            }).ToListAsync();

            Color accentColor = Color.ParseHex(character.VerticalPaintingColor);

            Image portraitImage = await portraitTask;
            Image weaponImage = await weaponTask;
            disposables.Add(portraitImage);
            disposables.Add(weaponImage);
            disposables.AddRange(diskImage);

            portraitImage.Mutate(ctx => ctx.Resize(2000, 0));

            background.Mutate(ctx =>
            {
                ctx.Clear(accentColor);

                ctx.DrawText(new RichTextOptions(m_ExtraLargeFont)
                {
                    Origin = new Vector2(-500, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    WrappingLength = 1800,
                    WordBreaking = WordBreaking.BreakAll
                }, string.Join($"{character.FullName.ToUpperInvariant()} ", Enumerable.Range(0, 5).Select(_ => "")),
                    Color.White.WithAlpha(0.25f));

                ctx.DrawImage(portraitImage,
                    new Point(350 - portraitImage.Width / 2, 650 - portraitImage.Height / 4), 1f);

                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(73, 53),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, character.Name!, Color.Black);
                ctx.DrawText(new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, character.Name!, Color.White);

                FontRectangle bounds = TextMeasurer.MeasureBounds(character.Name!, new RichTextOptions(m_TitleFont)
                {
                    Origin = new PointF(70, 50),
                    WrappingLength = 700,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                });

                ctx.DrawText($"Lv. {character.Level}", m_NormalFont, Color.Black,
                    new PointF(73, bounds.Bottom + 23));
                ctx.DrawText($"Lv. {character.Level}", m_NormalFont, Color.White,
                    new PointF(70, bounds.Bottom + 20));
                ctx.DrawText(gameUid, m_SmallFont, Color.White, new PointF(70, 1150));

                ctx.FillPolygon(BackgroundColor, new PointF(900, 0), new PointF(688, 1200), new PointF(3000, 1200), new PointF(3000, 0));

                foreach (Rank rank in character.Ranks)
                {
                    using Image<Rgba32> rankImage = CreateRankTemplateImage(rank.Id, rank.IsUnlocked, accentColor);
                    int yOffset = 130 * (rank.Id - 1);
                    ctx.DrawImage(rankImage, new Point(890 - (int)MathF.Round(yOffset * 0.1763f), yOffset), 1f);
                }

                for (int i = 0; i < diskImage.Count; i++)
                {
                    int offset = i * 170;
                    ctx.DrawImage(diskImage[i], new Point(2200, 50 + offset), 1f);
                }
            });

            MemoryStream stream = new();
            await background.SaveAsJpegAsync(stream, m_JpegEncoder);
            stream.Position = 0;
            return stream;
        }
        catch (Exception e)
        {
            m_Logger.LogError(e, "Failed to generate character card for {GameUid}, Data:{Data}\n",
                gameUid, JsonSerializer.Serialize(characterInformation));
            throw new CommandException("An error occurred while generating character card", e);
        }
        finally
        {
            disposables.ForEach(d => d.Dispose());
        }
    }

    private Image CreateDiskTemplateImageAsync()
    {
        Image<Rgba32> diskTemplate = m_DiskTemplate.Clone();
        diskTemplate.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new Vector2(425, 75),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }, "Not Equipped", Color.White);
        });

        return diskTemplate;
    }

    private async ValueTask<Image> CreateDiskImageAsync(DiskDrive disk)
    {
        Image diskImage = await Image.LoadAsync(await m_ImageRepository.DownloadFileToStreamAsync(
            string.Format(BasePath, disk.EquipSuit.SuitId)));
        Image<Rgba32> diskTemplate = m_DiskTemplate.Clone();
        diskTemplate.Mutate(ctx =>
        {
            ctx.DrawImage(diskImage, new Point(10, 0), 1f);
            ctx.DrawImage(m_RarityImages[disk.Rarity[0]], new Point(20, 115), 1f);
            ctx.DrawImage(m_StatImages[StatUtils.GetStatAssetName(disk.MainProperties[0].PropertyName)], new Point(125, 10), 1f);
            ctx.DrawText(new RichTextOptions(m_NormalFont)
            {
                Origin = new PointF(230, 60),
                HorizontalAlignment = HorizontalAlignment.Right
            }, disk.MainProperties[0]!.Base!, Color.White);
            ctx.DrawText($"+{disk.Level}", m_SmallFont, Color.White, new PointF(180, 20));
            // Draw properties
            for (int i = 0; i < disk.Properties!.Count; i++)
            {
                EquipProperty subStat = disk.Properties[i];
                Image subStatImage = m_StatImages[StatUtils.GetStatAssetName(subStat.PropertyName)];
                int xOffset = i % 2 * 245;
                int yOffset = i / 2 * 70;
                Color color = Color.White;
                if (subStat is { PropertyName: "ATK" or "DEF" or "HP" } && !subStat.Base.EndsWith('%'))
                {
                    Image<Rgba32> dim = subStatImage.CloneAs<Rgba32>();
                    dim.Mutate(x => x.Brightness(0.5f));
                    ctx.DrawImage(dim, new Point(260 + xOffset, 15 + yOffset), 1f);
                    color = Color.FromRgb(128, 128, 128);
                }
                else
                {
                    ctx.DrawImage(subStatImage, new Point(260 + xOffset, 15 + yOffset), 1f);
                }

                ctx.DrawText(subStat.Base!, m_NormalFont, color, new PointF(310 + xOffset, 20 + yOffset));
                string rolls = string.Concat(Enumerable.Repeat('.', subStat.Level));
                ctx.DrawText(rolls, m_NormalFont, color, new PointF(435 + xOffset, 10 + yOffset));
            }
        });
        return diskTemplate;
    }

    private Image<Rgba32> CreateRankTemplateImage(int rank, bool activated, Color accentColor)
    {
        Image<Rgba32> image = new(120, 150);

        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            IPath path = ImageUtility.CreateRoundedRectanglePath(90, 120, 10).Translate(15, 15);
            ctx.Fill(OverlayColor, path);
            ctx.Draw(accentColor, 4f, path);
            ctx.DrawText(new RichTextOptions(m_TitleFont)
            {
                Origin = new Vector2(60, 75),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }, rank.ToString("D2"), Color.White);
            if (!activated) ctx.Brightness(0.5f);

            ctx.Rotate(10, KnownResamplers.Bicubic);
        });
        return image;
    }
}
