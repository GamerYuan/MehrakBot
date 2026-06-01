#region

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

#endregion

namespace Mehrak.Application.Shared.Renderers.Extensions;

public record RosterLayout(
    int MaxSlots,
    int AvatarWidth = 150,
    int CanvasHeight = 200,
    int Spacing = 10);

public static class RosterImageBuilder
{
    public static Image<Rgba32> Build(
        IEnumerable<Image> avatars,
        RosterLayout layout,
        Image? trailingImage = null)
    {
        var avatarList = avatars as IReadOnlyList<Image> ?? [.. avatars];
        var totalSlots = avatarList.Count + (trailingImage != null ? 1 : 0);
        var canvasWidth = layout.MaxSlots * layout.AvatarWidth + (layout.MaxSlots - 1) * layout.Spacing + 20;

        var offset = (layout.MaxSlots - totalSlots) * layout.AvatarWidth / 2 + 10;

        Image<Rgba32> rosterImage = new(canvasWidth, layout.CanvasHeight, Color.Transparent.ToPixel<Rgba32>());

        rosterImage.Mutate(ctx =>
        {
            ctx.Paint(canvas =>
            {
                for (var i = 0; i < avatarList.Count; i++)
                {
                    var x = offset + i * (layout.AvatarWidth + layout.Spacing);
                    canvas.DrawImage(avatarList[i], avatarList[i].Bounds,
                        new RectangleF(x, 0, avatarList[i].Width, avatarList[i].Height), KnownResamplers.Bicubic);
                }

                if (trailingImage != null)
                {
                    var x = offset + avatarList.Count * (layout.AvatarWidth + layout.Spacing);
                    canvas.DrawImage(trailingImage, trailingImage.Bounds,
                        new RectangleF(x, 0, trailingImage.Width, trailingImage.Height), KnownResamplers.Bicubic);
                }
            });
        });

        return rosterImage;
    }
}
