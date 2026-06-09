using Mehrak.Infrastructure.CodeRedeem.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.CodeRedeem;

public class CodeRedeemDbContext(DbContextOptions<CodeRedeemDbContext> options) : DbContext(options)
{
    public DbSet<CodeRedeemModel> Codes { get; set; }
}
