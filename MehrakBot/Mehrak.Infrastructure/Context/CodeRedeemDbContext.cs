using Mehrak.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Mehrak.Infrastructure.Context;

internal class CodeRedeemDbContext(DbContextOptions<CodeRedeemDbContext> options) : DbContext(options)
{
    public DbSet<CodeRedeemModel> Codes { get; set; }
}
