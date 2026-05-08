using System.ComponentModel.DataAnnotations;

namespace Mehrak.Infrastructure.Models;

public class CharacterServerIdModel
{
    [Key]
    public int Id { get; set; }

    public int CharacterId { get; set; }
    public CharacterModel Character { get; set; } = null!;

    public int ServerId { get; set; }
}
