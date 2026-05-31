using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Character.Models;

[Index(nameof(Game), nameof(Name), IsUnique = true)]
public class CharacterModel
{
    [Key]
    public int Id { get; set; }

    public Game Game { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public float? BaseVal { get; set; }
    public float? MaxAscVal { get; set; }

    public List<CharacterServerIdModel> ServerIds { get; set; } = [];
}
