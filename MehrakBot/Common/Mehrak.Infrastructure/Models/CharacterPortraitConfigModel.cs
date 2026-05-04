using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Models;

[Index(nameof(Game), nameof(Name), IsUnique = true)]
public class CharacterPortraitConfigModel
{
    [Key]
    public int Id { get; set; }

    public Game Game { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int? OffsetX { get; set; }
    public int? OffsetY { get; set; }
    public float? TargetScale { get; set; }
    public bool? EnableGradientFade { get; set; }
    public float? GradientFadeStart { get; set; }
}
