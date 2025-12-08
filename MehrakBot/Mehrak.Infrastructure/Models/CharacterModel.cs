using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Models;

[Index(nameof(Game), nameof(Name), IsUnique = true)]
internal class CharacterModel
{
    [Key]
    public int Id { get; set; }

    public Game Game { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}
