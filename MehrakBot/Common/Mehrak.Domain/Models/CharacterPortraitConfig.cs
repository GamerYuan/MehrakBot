namespace Mehrak.Domain.Models;

public class CharacterPortraitConfig
{
    public int? OffsetX { get; init; }
    public int? OffsetY { get; init; }
    public float? TargetScale { get; init; }
    public bool? EnableGradientFade { get; init; }
    public float? GradientFadeStart { get; init; }
}

public class CharacterPortraitConfigUpdate
{
    public int? OffsetX { get; set; }
    public int? OffsetY { get; set; }
    public float? TargetScale { get; set; }
    public bool? EnableGradientFade { get; set; }
    public float? GradientFadeStart { get; set; }
}
