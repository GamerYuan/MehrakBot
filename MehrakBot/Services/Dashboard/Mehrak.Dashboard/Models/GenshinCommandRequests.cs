using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public abstract class GenshinBaseCommandRequest
{
    [Range(1, 10)]
    public int ProfileId { get; set; } = 1;

    [Required]
    [StringLength(32, MinimumLength = 2)]
    public string Server { get; set; } = string.Empty;
}

public sealed class GenshinCharacterRequest : GenshinBaseCommandRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Character { get; set; } = string.Empty;
}

public sealed class GenshinAbyssRequest : GenshinBaseCommandRequest
{
    [Range(9, 12, ErrorMessage = "Floor must be between 9 and 12.")]
    public uint Floor { get; set; }
}

public sealed class GenshinSimpleCommandRequest : GenshinBaseCommandRequest;
