using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public abstract class CodeUpdateRequestBase
{
    [Required]
    [StringLength(32, MinimumLength = 2)]
    public string Game { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<string> Codes { get; set; } = [];
}

public sealed class AddCodesRequest : CodeUpdateRequestBase;

public sealed class RemoveCodesRequest : CodeUpdateRequestBase;
