#region

using Mehrak.Application.Shared.Abstractions;
using Mehrak.Domain.Image;
using Mehrak.Domain.Image.Models;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Hsr.EndGame;

internal class HsrPureFictionCardService : HsrEndGameCardServiceBase
{
    private Image m_PfBackground = null!;

    public HsrPureFictionCardService(IImageRepository imageRepository,
        ILogger<HsrPureFictionCardService> logger,
        IApplicationMetrics metrics)
        : base("Hsr PF", imageRepository, logger, metrics)
    {
    }

    protected override async Task LoadModeResourcesAsync(CancellationToken cancellationToken)
    {
        m_PfBackground = await Image.LoadAsync<Rgba32>(
            await ImageRepository.DownloadFileToStreamAsync(FileNameFormat.Hsr.PFBackgroundName, cancellationToken),
            cancellationToken);
    }

    protected override Image GetModeBackgroundImage() => m_PfBackground;

    protected override string GetModeString() => "Pure Fiction";

    protected override List<(int FloorNumber, HsrEndFloorDetail? Data)> GetFloorDetails(
        HsrEndInformation gameModeData)
    {
        return
        [
            .. Enumerable.Range(0, 4)
                .Select(floorIndex =>
                {
                    var floorData = gameModeData.AllFloorDetail
                        .FirstOrDefault(x => HsrUtility.GetFloorNumber(x.Name) - 1 == floorIndex);
                    return (FloorNumber: floorIndex, Data: floorData);
                })
        ];
    }

    protected override string GetStageText(HsrEndInformation gameModeData, int floorNumber)
    {
        return $"{gameModeData.Groups[0].Name} ({HsrUtility.GetRomanNumeral(floorNumber + 1)})";
    }

    protected override void DrawScoreExtras(DrawingCanvas canvas, int xOffset, int yOffset,
        string scoreText, HsrEndFloorDetail floorData, HsrEndInformation gameModeData)
    {
        var size = TextMeasurer.MeasureBounds(scoreText, new TextOptions(Fonts.Normal));
        canvas.Draw(Pens.Solid(Color.White, 2f), new PathBuilder().AddLine(
            new PointF(xOffset + 695 - (int)size.Width, yOffset + 10),
            new PointF(xOffset + 695 - (int)size.Width, yOffset + 55)).Build());

        canvas.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new PointF(xOffset + 650 - (int)size.Width, yOffset + 20),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        }, floorData.RoundNum.ToString(), Brushes.Solid(Color.White), null);
        canvas.DrawImage(CycleIcon, CycleIcon.Bounds,
            new RectangleF(xOffset + 650 - (int)size.Width, yOffset + 10, CycleIcon.Width, CycleIcon.Height),
            KnownResamplers.Bicubic);
    }
}
