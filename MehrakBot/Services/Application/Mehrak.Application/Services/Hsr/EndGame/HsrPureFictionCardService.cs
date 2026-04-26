#region

using Mehrak.Application.Services.Abstractions;
using Mehrak.Domain.Repositories;
using Mehrak.GameApi.Hsr.Types;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Services.Hsr.EndGame;

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
            await ImageRepository.DownloadFileToStreamAsync("hsr_pf_bg", cancellationToken),
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

    protected override void DrawScoreExtras(IImageProcessingContext ctx, int xOffset, int yOffset,
        string scoreText, HsrEndFloorDetail floorData, HsrEndInformation gameModeData)
    {
        var size = TextMeasurer.MeasureSize(scoreText, new TextOptions(Fonts.Normal));
        ctx.DrawLine(Color.White, 2f, new PointF(xOffset + 695 - (int)size.Width, yOffset + 10),
            new PointF(xOffset + 695 - (int)size.Width, yOffset + 55));

        ctx.DrawText(new RichTextOptions(Fonts.Normal)
        {
            Origin = new PointF(xOffset + 650 - (int)size.Width, yOffset + 20),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        }, floorData.RoundNum.ToString(), Color.White);
        ctx.DrawImage(CycleIcon, new Point(xOffset + 650 - (int)size.Width, yOffset + 10), 1f);
    }
}
