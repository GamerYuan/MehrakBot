#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using MehrakCore.Utility;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace MehrakCore.Services.Genshin;

public class GenshinCharacterCardService : ICharacterCardService<GenshinCharacterInformation>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharacterCardService> m_Logger;

    private const string BasePath = "genshin_{0}";

    public GenshinCharacterCardService(ImageRepository imageRepository, ILogger<GenshinCharacterCardService> logger)
    {
        m_ImageRepository = imageRepository;
        m_Logger = logger;
    }

    public async Task<Stream> GenerateCharacterCardAsync(GenshinCharacterInformation charInfo)
    {
        try
        {
            m_Logger.LogInformation("Fetching background image for {Element} character card", charInfo.Base.Element);
            var overlay =
                Image.Load(await m_ImageRepository.DownloadFileAsBytesAsync($"bg.png"));
            var background = new Image<Rgba32>(1620, 540);
            var characterPortrait =
                Image.Load<Rgba32>(
                    await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, charInfo.Base.Id)));
            var weaponImage =
                Image.Load<Rgba32>(
                    await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, charInfo.Base.Weapon.Id)));
            var constellationIcons =
                charInfo.Constellations.Select(async x =>
                        Image.Load(await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, x.Id))))
                    .ToArray();
            var skillIcons = charInfo.Skills.Select(async x =>
                    Image.Load(await m_ImageRepository.DownloadFileAsBytesAsync(string.Format(BasePath, x.SkillId))))
                .ToArray();

            background.Mutate(ctx =>
            {
                ctx.Fill(GetBackgroundColor(charInfo.Base.Element));
                ctx.DrawImage(overlay, PixelColorBlendingMode.Overlay, 1f);

                FontCollection collection = new();
                var fontFamily = collection.Add("Fonts/Futura Md BT Bold.ttf");
                var titleFont = fontFamily.CreateFont(32, FontStyle.Regular);
                var font = fontFamily.CreateFont(20, FontStyle.Regular);
                var textColor = Color.White;

                ctx.DrawText(charInfo.Base.Name, titleFont, textColor, new PointF(50, 40));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", font, textColor, new PointF(50, 80));
                ctx.DrawImage(characterPortrait, new Point(-100, 120), 1f);

                for (int i = 2; i >= 0; i--)
                {
                    var skillIcon = skillIcons[i].Result;
                    skillIcon.Mutate(x => x.Resize(new Size(50, 0), KnownResamplers.Bicubic, true));
                    ctx.DrawImage(skillIcon, new Point(20, 480 - i * 50), 1f);
                }

                for (int i = constellationIcons.Length - 1; i >= 0; i--)
                {
                    var constellationIcon = constellationIcons[i].Result;
                    constellationIcon.Mutate(x => x.Resize(new Size(50, 0), KnownResamplers.Bicubic, true));
                    if (!charInfo.Constellations[i].IsActived!.Value)
                        constellationIcon.Mutate(x => x.Brightness(0.65f));
                    ctx.DrawImage(constellationIcon, new Point(500, 480 - i * 50), 1f);
                }

                weaponImage.Mutate(x => x.Resize(new Size(200, 0), KnownResamplers.Bicubic, true));
                ctx.DrawImage(weaponImage, new Point(550, 0), 1f);
                ctx.DrawText(charInfo.Base.Weapon.Name, font, textColor, new PointF(800, 40));
                ctx.DrawText('R' + charInfo.Base.Weapon.AffixLevel!.Value.ToString(), font, textColor,
                    new PointF(800, 80));
                ctx.DrawText($"Lv. {charInfo.Base.Weapon.Level}", font, textColor, new PointF(850, 80));

                var stats = charInfo.BaseProperties.Take(3).Concat(charInfo.SelectedProperties)
                    .DistinctBy(x => x.PropertyType)
                    .Where(x => float.Parse(x.Final.TrimEnd('%')) >
                                StatMappingUtility.GetDefaultValue(x.PropertyType!.Value))
                    .Select(x => (Stat: StatMappingUtility.Mapping[x.PropertyType!.Value], Val: x.Final)).ToArray();
                var spacing = 280 / stats.Length;

                for (int i = 0; i < stats.Length; i++)
                {
                    var stat = stats[i];
                    ctx.DrawText(stat.Stat, font, textColor, new PointF(600, 240 + spacing * i));
                    ctx.DrawText(stat.Val, font, textColor, new PointF(1000, 240 + spacing * i));
                }
            });

            var stream = new MemoryStream();
            await background.SaveAsJpegAsync(stream);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            m_Logger.LogError("Error generating character card for {CharacterName}", charInfo.Base.Name);
            throw;
        }
    }

    private Color GetBackgroundColor(string element)
    {
        return element switch
        {
            "Pyro" => Color.ParseHex("#BF8667"),
            "Hydro" => Color.ParseHex("#7A92FF"),
            "Electro" => Color.ParseHex("#9E65C8"),
            "Dendro" => Color.ParseHex("#529D62"),
            "Cryo" => Color.ParseHex("#78CACC"),
            "Geo" => Color.ParseHex("#B5A155"),
            "Anemo" => Color.ParseHex("7fB29E"),
            _ => Color.White
        };
    }
}
