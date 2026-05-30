using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Character.Models;

[Index(nameof(CharacterId), nameof(ServerId), IsUnique = true)]
public class CharacterServerIdModel
{
    [Key]
    public int Id { get; set; }

    public int CharacterId { get; set; }
    public CharacterModel Character { get; set; } = null!;

    public int ServerId { get; set; }
}
