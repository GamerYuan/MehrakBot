#region

using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Enums;
using Microsoft.EntityFrameworkCore;

#endregion

namespace Mehrak.Infrastructure.Models;

[Index(nameof(Game), nameof(Alias), IsUnique = true)]
public class AliasModel
{
    [Key]
    public int Id { get; set; }

    public Game Game { get; set; }

    [MaxLength(20)]
    public string Alias { get; set; } = string.Empty;

    [MaxLength(100)]
    public string CharacterName { get; set; } = string.Empty;
}
