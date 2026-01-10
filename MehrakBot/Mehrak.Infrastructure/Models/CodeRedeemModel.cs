#region

using System.ComponentModel.DataAnnotations;
using Mehrak.Domain.Enums;
using Microsoft.EntityFrameworkCore;

#endregion

namespace Mehrak.Infrastructure.Models;

[Index(nameof(Game), nameof(Code), IsUnique = true)]
public class CodeRedeemModel
{
    [Key]
    public int Id { get; set; }

    public Game Game { get; set; }

    public string Code { get; set; } = string.Empty;
}
