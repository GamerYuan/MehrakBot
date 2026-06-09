#region

using System.ComponentModel.DataAnnotations;

#endregion

namespace Mehrak.Domain.Character.Models;

public class HsrRelicModel
{
    [Key]
    public int SetId { get; set; }

    public required string SetName { get; set; }
}
