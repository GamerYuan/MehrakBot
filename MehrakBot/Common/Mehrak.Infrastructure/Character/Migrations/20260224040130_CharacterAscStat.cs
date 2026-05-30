using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.CharacterDb
{
    /// <inheritdoc />
    public partial class CharacterAscStat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "BaseVal",
                table: "Characters",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "MaxAscVal",
                table: "Characters",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseVal",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "MaxAscVal",
                table: "Characters");
        }
    }
}
