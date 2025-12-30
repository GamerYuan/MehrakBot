using System.ComponentModel.DataAnnotations;

namespace Mehrak.Dashboard.Models;

public class AddCharactersRequest
{
    [Required]
    public IEnumerable<string> Characters { get; set; } = [];
}
