#region

using System.ComponentModel.DataAnnotations;

#endregion

namespace Mehrak.Domain.Models;

public class HsrRelicModel
{
    [Key]
    public int SetId { get; set; }

    public required string SetName { get; set; }
}
