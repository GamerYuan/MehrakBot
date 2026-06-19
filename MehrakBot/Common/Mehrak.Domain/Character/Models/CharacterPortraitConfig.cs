using System.ComponentModel.DataAnnotations;

namespace Mehrak.Domain.Character.Models;

public class CharacterPortraitConfig
{
    public int? ServerId { get; init; }
    public int? OffsetX { get; init; }
    public int? OffsetY { get; init; }
    public float? TargetScale { get; init; }
}

public class CharacterPortraitConfigUpdate
{
    public int? OffsetX { get; set; }
    public int? OffsetY { get; set; }

    [Range(0.01f, 10f, ErrorMessage = "Scale must be between 0 and 10.")]
    public float? TargetScale { get; set; }
}
