using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Character.Models;

[Index(nameof(Game), nameof(ServerId), IsUnique = true)]
public class CharacterPortraitConfigModel
{
    [Key]
    public int Id { get; set; }

    public Game Game { get; set; }

    public int ServerId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int? OffsetX { get; set; }
    public int? OffsetY { get; set; }
    public float? TargetScale { get; set; }
}
