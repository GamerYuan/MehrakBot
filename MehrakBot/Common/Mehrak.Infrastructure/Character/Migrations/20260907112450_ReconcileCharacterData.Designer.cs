using Mehrak.Infrastructure.Character;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Character.Migrations;

[DbContext(typeof(CharacterDbContext))]
[Migration("20260907112450_ReconcileCharacterData")]
partial class ReconcileCharacterData
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
    }
}
