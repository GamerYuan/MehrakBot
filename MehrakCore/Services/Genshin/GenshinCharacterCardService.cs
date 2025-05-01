#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

#endregion

namespace MehrakCore.Services.Genshin;

public class GenshinCharacterCardService : ICharacterCardService<GenshinCharacterInformation>
{
    private readonly ImageRepository m_ImageRepository;
    private readonly ILogger<GenshinCharacterCardService> m_Logger;

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
            var background = new Image<Rgba32>(overlay.Width, overlay.Height);
            var characterPortrait =
                Image.Load<Rgba32>(await m_ImageRepository.DownloadFileAsBytesAsync($"{charInfo.Base.Id}.png"));

            background.Mutate(ctx =>
            {
                ctx.Fill(GetAdaptiveBackgroundColor(characterPortrait));
                ctx.DrawImage(overlay, PixelColorBlendingMode.Overlay, 1f);

                FontCollection collection = new();
                var fontFamily = collection.Add("Fonts/Futura Md BT Bold.ttf");
                var font = fontFamily.CreateFont(20, FontStyle.Regular);
                var textColor = Color.White;

                ctx.DrawText(charInfo.Base.Name, font, textColor, new PointF(50, 50));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", font, textColor, new PointF(50, 100));
                ctx.DrawImage(characterPortrait, new Point(50, 150), 1f);
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

    private Rgba32 GetAdaptiveBackgroundColor(Image<Rgba32> portraitImage)
    {
        try
        {
            using var smallImage = portraitImage.Clone();
            smallImage.Mutate(x => x.Resize(new ResizeOptions
                    { Sampler = KnownResamplers.NearestNeighbor, Size = new Size(100, 0) })
                .Quantize(new WuQuantizer(new QuantizerOptions { MaxColors = 5 })));

            Span<Rgba32> pixels = stackalloc Rgba32[smallImage.Width * smallImage.Height];
            smallImage.CopyPixelDataTo(pixels);
            var palette = pixels.ToArray().GroupBy(x => x)
                .Select(x => x.Key).Select(x => new Rgb24(x.R, x.G, x.B)).ToArray();

            var adaptiveColor = palette[2];
            var hslColor = ColorSpaceConverter.ToHsl(adaptiveColor);

            // Simple contrast logic: If the color is light, make it darker; if dark, make it lighter.
            // Adjust saturation slightly to avoid overly vibrant backgrounds.
            var adjustedSaturation = Math.Max(0.1f, hslColor.S * 0.8f); // Reduce saturation, keep some color

            var adjustedLightness =
                // It's a light color, make it darker for the background
                hslColor.L > 0.5f
                    ? Math.Max(0.1f, hslColor.L * 0.8f)
                    : // Significantly darken
                    // It's a dark color, make it lighter for the background
                    Math.Min(0.9f, hslColor.L * 1.15f); // Significantly lighten

            var adjustedColor = new Hsl(hslColor.H, adjustedSaturation, adjustedLightness);

            return ColorSpaceConverter.ToRgb(adjustedColor);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting adaptive background color: {ex.Message}");
            return Color.Gray; // Return default on any error
        }
    }
}
