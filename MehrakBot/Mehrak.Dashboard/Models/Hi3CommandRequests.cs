using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public abstract class Hi3BaseCommandRequest
{
    [Range(1, 10)]
    public int ProfileId { get; set; } = 1;

    [Required]
    [StringLength(32, MinimumLength = 2)]
    public string Server { get; set; } = string.Empty;
}

public sealed class Hi3BattlesuitRequest : Hi3BaseCommandRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Battlesuit { get; set; } = string.Empty;
}
