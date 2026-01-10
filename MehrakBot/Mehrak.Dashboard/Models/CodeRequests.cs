using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public abstract class CodeUpdateRequestBase
{
    [Required]
    [MinLength(1)]
    public List<string> Codes { get; set; } = [];
}

public sealed class AddCodesRequest : CodeUpdateRequestBase;

public sealed class RemoveCodesRequest : CodeUpdateRequestBase;
