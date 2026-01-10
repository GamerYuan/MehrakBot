using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class AddCharactersRequest
{
    [Required, MinLength(1)]
    public IEnumerable<string> Characters { get; set; } = [];
}
