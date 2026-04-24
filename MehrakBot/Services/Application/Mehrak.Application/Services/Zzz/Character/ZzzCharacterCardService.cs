#region

using System.Numerics;
using Mehrak.Application.Extensions;
using Mehrak.Application.Renderers;
using Mehrak.Application.Renderers.Extensions;
using Mehrak.Application.Services.Abstractions;
using Mehrak.Application.Utility;
using Mehrak.Domain.Common;
using Mehrak.Domain.Models.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.Domain.Utility;
using Mehrak.GameApi.Zzz.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Zzz.Character;

internal class ZzzCharacterCardService : CardServiceBase<ZzzFullAvatarData>
{
    private Dictionary<string, Image> m_StatImages = [];
    private Dictionary<int, Image> m_SkillImages = [];
    private Dictionary<string, Image> m_AttributeImages = [];
    private Dictionary<int, Image> m_ProfessionImages = [];
    private Dictionary<char, Image> m_RarityImages = [];
    private Dictionary<int, Image> m_WeaponStarImages = [];

    private readonly Font m_ExtraLargeFont;

    private readonly Image m_WeaponTemplate;
    private readonly Image m_DiskBackground;
    private readonly Image<Rgba32> m_DiskTemplate;

    private static readonly Color LocalBackgroundColor = Color.FromRgb(69, 69, 69);
    private static readonly Color LocalOverlayColor = Color.FromRgb(36, 36, 36);

    public ZzzCharacterCardService(IImageRepository imageRepository,
        ILogger<ZzzCharacterCardService> logger,
        IApplicationMetrics metrics)
        : base(
            "Zzz Character",
            imageRepository,
            logger,
            metrics,
            LoadFonts("Assets/Fonts/zzz.ttf", titleSize: 64, normalSize: 40, mediumSize: 36, smallSize: 28, tinySize: 20))
    {
        m_ExtraLargeFont = new FontCollection().Add("Assets/Fonts/anton.ttf").CreateFont(400);

        m_WeaponTemplate = new Image<Rgba32>(100, 100);

        m_DiskBackground = new Image<Rgba32>(800, 170);
        m_DiskBackground.Mutate(ctx =>
        {
            ctx.Fill(LocalOverlayColor);
            ctx.ApplyRoundedCorners(30);
        });
        m_DiskTemplate = CreateDiskTemplateImage();
    }

    public override async Task LoadStaticResourcesAsync(CancellationToken cancellationToken = default)
    {
        var files = await ImageRepository.ListFilesAsync("zzz_stats");
        List<Task<(string x, Image)>> tasks =
        [
            .. files.Select(async file =>
                (file.Replace("zzz_stats_", "").TrimStart(),
                    await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(file, cancellationToken), cancellationToken)))
        ];

        int[] skillIds = [0, 1, 2, 3, 5, 6];
        List<Task<(int x, Image)>> skillTasks =
        [
            .. skillIds.Select(async id =>
                (id, await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                    string.Format(FileNameFormat.Zzz.SkillName, id), cancellationToken), cancellationToken)))
        ];

        List<Task<(string x, Image)>> attributeTasks =
        [
            .. (await ImageRepository.ListFilesAsync("zzz_attribute")).Select(async file =>
                (file.Replace("zzz_attribute_", "").TrimStart(),
                    await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(file, cancellationToken), cancellationToken)))
        ];

        List<Task<(int x, Image)>> professionTasks =
        [
            .. Enumerable.Range(1, 6).Select(async i =>
                (i, await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                    string.Format(FileNameFormat.Zzz.ProfessionName, i), cancellationToken), cancellationToken)))
        ];

        char[] itemRarity = ['s', 'a', 'b'];
        List<Task<(char x, Image)>> rarityTasks =
        [
            .. itemRarity.Select(async i =>
                (i, await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                    string.Format("zzz_rarity_{0}", i), cancellationToken), cancellationToken)))
        ];

        List<Task<(int x, Image)>> weaponStarTasks =
        [
            .. Enumerable.Range(1, 5)
                .Select(async i => (i, await Image.LoadAsync(
                    await ImageRepository.DownloadFileToStreamAsync($"zzz_weapon_star_{i}", cancellationToken), cancellationToken)))
        ];

        m_StatImages = (await Task.WhenAll(tasks)).ToDictionary(StringComparer.OrdinalIgnoreCase);
        m_SkillImages = (await Task.WhenAll(skillTasks)).ToDictionary();
        m_AttributeImages = (await Task.WhenAll(attributeTasks)).ToDictionary();
        m_ProfessionImages = (await Task.WhenAll(professionTasks)).ToDictionary();
        m_RarityImages = (await Task.WhenAll(rarityTasks)).ToDictionary(CaseInsensitiveCharComparer.Instance);
        m_WeaponStarImages = (await Task.WhenAll(weaponStarTasks)).ToDictionary();
    }

    protected override Image<Rgba32> CreateBackground()
    {
        return new Image<Rgba32>(3000, 1200);
    }

    public override async Task RenderCardAsync(
        Image<Rgba32> background,
        ICardGenerationContext<ZzzFullAvatarData> context,
        DisposableBag disposables,
        CancellationToken cancellationToken = default)
    {
        var characterInformation = context.Data;
        var character = characterInformation.AvatarList[0];

        var portraitTask = Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
            string.Format(character.ToImageName()), cancellationToken), cancellationToken);
        var weaponTask = character.Weapon == null
            ? Task.FromResult(m_WeaponTemplate.Clone(ctx => { }))
            : Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
                string.Format(character.Weapon.ToImageName()), cancellationToken), cancellationToken);
        var diskImage = await Enumerable.Range(1, 6).ToAsyncEnumerable()
            .Select(async (i, token) =>
            {
                var disk = character.Equip.FirstOrDefault(x => x.EquipmentType == i);
                if (disk == null)
                    return m_DiskTemplate.CloneAs<Rgba32>();
                else
                    return await CreateDiskImageAsync(disk, token);
            }).ToListAsync();

        var accentColor = Color.ParseHex(character.VerticalPaintingColor);

        var portraitImage = await portraitTask;
        var weaponImage = await weaponTask;
        disposables.Add(portraitImage);
        disposables.Add(weaponImage);
        disposables.AddRange(diskImage);

        background.Mutate(ctx =>
        {
            ctx.Clear(accentColor);

            ctx.DrawFauxItalicText(new RichTextOptions(m_ExtraLargeFont)
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

            ctx.DrawTextWithShadow(character.Name!, new RichTextOptions(Fonts.Title)
            {
                Origin = new PointF(70, 50),
                WrappingLength = 700,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, Color.White);

            var bounds = TextMeasurer.MeasureBounds(character.Name!, new RichTextOptions(Fonts.Title)
            {
                Origin = new PointF(70, 50),
                WrappingLength = 700,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            });

            ctx.DrawTextWithShadow($"Lv. {character.Level}", Fonts.Normal,
                new PointF(70, bounds.Bottom + 10), Color.White);

            ctx.DrawTextWithShadow(context.GameProfile.Nickname, Fonts.Normal, new PointF(70, 1100), Color.White);
            ctx.DrawTextWithShadow(context.GameProfile.GameUid, Fonts.Small, new PointF(70, 1140), Color.White);

            ctx.FillPolygon(LocalBackgroundColor, new PointF(900, 0), new PointF(688, 1200), new PointF(3000, 1200),
                new PointF(3000, 0));

            foreach (var rank in character.Ranks)
            {
                using var rankImage = CreateRankTemplateImage(rank.Id, rank.IsUnlocked, accentColor);
                var yOffset = 130 * (rank.Id - 1);
                ctx.DrawImage(rankImage, new Point(890 - (int)MathF.Round(yOffset * 0.1763f), yOffset), 1f);
            }

            using var professionImage =
                CreateRotatedIconImage(m_ProfessionImages[character.AvatarProfession], accentColor);
            ctx.DrawImage(professionImage, new Point(890 - (int)MathF.Round(1030 * 0.1763f), 1030), 1f);

            using var elementImage =
                CreateRotatedIconImage(
                    m_AttributeImages[
                        StatUtils.GetElementNameFromId(character.ElementType, character.SubElementType)],
                    accentColor);
            ctx.DrawImage(elementImage, new Point(890 - (int)MathF.Round(900 * 0.1763f), 900), 1f);

            foreach (var skill in character.Skills)
            {
                var skillIndex = skill.SkillType == 6 ? 4 : skill.SkillType;
                var yOffset = skillIndex >= 3 ? 130 : 0;
                var xOffset = skillIndex % 3 * 120;
                ctx.DrawImage(m_SkillImages[skill.SkillType],
                    new Point(1030 + xOffset, 70 + yOffset), 1f);
                ctx.DrawCenteredTextInEllipse(
                    skill.Level.ToString(),
                    new PointF(1110 + xOffset, 150 + yOffset),
                    25,
                    new EllipseTextStyle(
                        Fonts.Small,
                        Color.White,
                        LocalOverlayColor,
                        accentColor,
                        4f));
            }

            ctx.DrawRoundedRectangleOverlay(450, 330, new PointF(950, 690),
                new RoundedRectangleOverlayStyle(LocalOverlayColor, accentColor, 4f, 30));

            if (character.Weapon != null)
            {
                ctx.DrawImage(weaponImage, new Point(970, 710), 1f);
                ctx.DrawImage(m_RarityImages[character.Weapon.Rarity[0]], new Point(970, 720), 1f);
                ctx.DrawImage(m_WeaponStarImages[character.Weapon.Star], new Point(970, 820), 1f);

                ctx.DrawText($"Lv.{character.Weapon.Level}", Fonts.Medium!, Color.White, new PointF(980, 880));

                ctx.DrawText(new RichTextOptions(character.Weapon.Name.Length > 40 ? Fonts.Small : Fonts.Medium)
                {
                    Origin = new Vector2(1120, 730),
                    WrappingLength = 280,
                    VerticalAlignment = VerticalAlignment.Top
                }, character.Weapon.Name, Color.White);
                ctx.DrawImage(
                    m_StatImages[StatUtils.GetStatAssetName(character.Weapon.MainProperties[0].PropertyName)],
                    new Point(980, 930), 1f);
                ctx.DrawText(character.Weapon.MainProperties[0].Base, Fonts.Medium!, Color.White,
                    new PointF(1030, 945));

                ctx.DrawImage(m_StatImages[StatUtils.GetStatAssetName(character.Weapon.Properties[0].PropertyName)],
                    new Point(1175, 930), 1f);
                ctx.DrawText(character.Weapon.Properties[0].Base, Fonts.Medium!, Color.White, new PointF(1225, 955));
            }
            else
            {
                ctx.DrawText(new RichTextOptions(Fonts.Medium!)
                {
                    Origin = new Vector2(1175, 848),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    WrappingLength = 350
                }, "No W-Engine Equipped", Color.White);
            }

            ctx.DrawRoundedRectangleOverlay(700, 970, new PointF(1420, 50),
                new RoundedRectangleOverlayStyle(LocalOverlayColor, accentColor, 4f, 30));

            var offsetInterval = 880 / (character.Properties.Count - 1);
            var statsYOffset = 0;

            for (var i = 0; i < character.Properties.Count; i++)
            {
                var stat = character.Properties[i];

                ctx.DrawStatLine(
                    new StatLineData(
                        stat.PropertyName,
                        stat.Final!,
                        stat.Base,
                        $"+{stat.Add}"),
                    new StatLineStyle(
                        m_StatImages.GetValueOrDefault(StatUtils.GetStatAssetName(stat.PropertyName)),
                        Fonts.Medium!,
                        Color.White,
                        Fonts.Tiny!,
                        Color.LightSlateGrey,
                        Color.LightGreen),
                    new PointF(1440, 75 + statsYOffset),
                    660);

                statsYOffset += offsetInterval;
            }

            EquipSuit[] activeSets =
                [.. character.Equip.Select(x => x.EquipSuit).DistinctBy(x => x.SuitId).Where(x => x.Own >= 2)];
            for (var i = 0; i < activeSets.Length; i++)
            {
                var yOffset = i * 50;
                var set = activeSets[i];
                ctx.DrawText(new RichTextOptions(Fonts.Small)
                {
                    Origin = new Vector2(2100, 1050 + yOffset),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, $"{set.Name}\tx{set.Own}", Color.White);
            }

            if (activeSets.Length == 0)
                ctx.DrawText(new RichTextOptions(Fonts.Small)
                {
                    Origin = new Vector2(2100, 1050),
                    HorizontalAlignment = HorizontalAlignment.Right
                }, "No Active Set", Color.White);

            for (var i = 0; i < diskImage.Count; i++)
            {
                var offset = i * 186;
                ctx.DrawImage(diskImage[i], new Point(2150, 50 + offset), 1f);
            }
        });
    }

    private Image<Rgba32> CreateDiskTemplateImage()
    {
        var diskTemplate = m_DiskBackground.CloneAs<Rgba32>();
        diskTemplate.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new Vector2(425, 78),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }, "Not Equipped", Color.White);
        });
        return diskTemplate;
    }

    private async ValueTask<Image> CreateDiskImageAsync(DiskDrive disk, CancellationToken token = default)
    {
        var diskImage = await Image.LoadAsync(await ImageRepository.DownloadFileToStreamAsync(
            disk.ToImageName(), token), token);
        var diskTemplate = m_DiskBackground.CloneAs<Rgba32>();
        diskTemplate.Mutate(ctx =>
        {
            ctx.DrawImage(diskImage, new Point(10, 15), 1f);
            ctx.DrawImage(m_RarityImages[disk.Rarity[0]], new Point(20, 115), 1f);
            ctx.DrawImage(m_StatImages[StatUtils.GetStatAssetName(disk.MainProperties[0].PropertyName)],
                new Point(215, 20), 1f);
            ctx.DrawText(new RichTextOptions(Fonts.Normal)
            {
                Origin = new PointF(265, 70),
                HorizontalAlignment = HorizontalAlignment.Right
            }, disk.MainProperties[0]!.Base!, Color.White);
            ctx.DrawText(new RichTextOptions(Fonts.Small)
            {
                Origin = new PointF(265, 120),
                HorizontalAlignment = HorizontalAlignment.Right
            }, $"Lv.{disk.Level}", Color.White);

            for (var i = 0; i < disk.Properties!.Count; i++)
            {
                var subStat = disk.Properties[i];
                var subStatImage = m_StatImages[StatUtils.GetStatAssetName(subStat.PropertyName)];
                var xOffset = i % 2 * 260;
                var yOffset = i / 2 * 85;
                var color = Color.White;
                if (subStat is { PropertyName: "ATK" or "DEF" or "HP" } && !subStat.Base.EndsWith('%'))
                {
                    var dim = subStatImage.CloneAs<Rgba32>();
                    dim.Mutate(x => x.Brightness(0.5f));
                    ctx.DrawImage(dim, new Point(280 + xOffset, 20 + yOffset), 1f);
                    color = Color.FromRgb(128, 128, 128);
                }
                else
                {
                    ctx.DrawImage(subStatImage, new Point(280 + xOffset, 20 + yOffset), 1f);
                }

                ctx.DrawText(subStat.Base!, Fonts.Normal, color, new PointF(335 + xOffset, 23 + yOffset));
                var rolls = string.Concat(Enumerable.Repeat('.', subStat.Level));
                ctx.DrawText(rolls, Fonts.Normal, color, new PointF(460 + xOffset, 8 + yOffset));
            }

            diskImage.Dispose();
        });
        return diskTemplate;
    }

    private Image<Rgba32> CreateRankTemplateImage(int rank, bool activated, Color accentColor)
    {
        Image<Rgba32> image = new(120, 150);
        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            if (!activated) ctx.Brightness(2f);
            ctx.DrawRoundedRectangleOverlay(90, 120, new PointF(15, 15),
                new RoundedRectangleOverlayStyle(LocalOverlayColor, accentColor, 4f, 10));
            ctx.DrawText(new RichTextOptions(Fonts.Title)
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

    private static Image<Rgba32> CreateRotatedIconImage(Image icon, Color accentColor)
    {
        Image<Rgba32> image = new(120, 150);
        image.Mutate(ctx =>
        {
            ctx.Clear(Color.Transparent);
            ctx.DrawRoundedRectangleOverlay(90, 120, new PointF(15, 15),
                new RoundedRectangleOverlayStyle(LocalOverlayColor, accentColor, 4f, 10));
            ctx.DrawImage(icon, new Point(60 - icon.Width / 2, 75 - icon.Height / 2), 1f);
            ctx.Rotate(10, KnownResamplers.Bicubic);
        });
        return image;
    }
}
