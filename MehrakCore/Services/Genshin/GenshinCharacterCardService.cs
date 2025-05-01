#region

using MehrakCore.ApiResponseTypes.Genshin;
using MehrakCore.Repositories;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

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
            var backgroundImage =
                Image.Load(await m_ImageRepository.DownloadFileAsBytesAsync($"{charInfo.Base.Element}.jpeg"));

            var characterPortrait =
                Image.Load(await m_ImageRepository.DownloadFileAsBytesAsync($"{charInfo.Base.Id}.png"));

            backgroundImage.Mutate(ctx =>
            {
                FontCollection collection = new();
                var fontFamily = collection.Add("Fonts/Futura Md BT Bold.ttf");
                var font = fontFamily.CreateFont(20, FontStyle.Regular);
                Color textColor = Color.White;

                ctx.DrawText(charInfo.Base.Name, font, textColor, new PointF(300, 50));
                ctx.DrawText($"Lv. {charInfo.Base.Level}", font, textColor, new PointF(300, 100));
                ctx.DrawImage(characterPortrait, new Point(100, 150), 1f);
            });

            var stream = new MemoryStream();
            await backgroundImage.SaveAsJpegAsync(stream);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            m_Logger.LogError("Error generating character card for {CharacterName}", charInfo.Base.Name);
            throw;
        }
    }
}
