using System.ComponentModel.DataAnnotations;

namespace Mehrak.Infrastructure.Character.Models;

public class UserPortraitConfigModel
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserPortraitUploadId { get; set; }

    public UserPortraitUpload Upload { get; set; } = default!;

    public int? OffsetX { get; set; }
    public int? OffsetY { get; set; }
    public float? TargetScale { get; set; }
    public bool? EnableGradientFade { get; set; }
    public float? GradientFadeStart { get; set; }
}
