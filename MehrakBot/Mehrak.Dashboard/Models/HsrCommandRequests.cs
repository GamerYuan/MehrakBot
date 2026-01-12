using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public abstract class HsrBaseCommandRequest
{
    [Range(1, uint.MaxValue)]
    public uint ProfileId { get; set; } = 1;

    [Required]
    [StringLength(32, MinimumLength = 2)]
    public string Server { get; set; } = string.Empty;
}

public sealed class HsrCharacterRequest : HsrBaseCommandRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Character { get; set; } = string.Empty;
}

public sealed class HsrSimpleCommandRequest : HsrBaseCommandRequest;
