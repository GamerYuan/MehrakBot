using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.CharacterDb
{
    /// <inheritdoc />
    public partial class CharacterPortraitConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterPortraitConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Game = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OffsetX = table.Column<int>(type: "integer", nullable: true),
                    OffsetY = table.Column<int>(type: "integer", nullable: true),
                    TargetScale = table.Column<float>(type: "real", nullable: true),
                    EnableGradientFade = table.Column<bool>(type: "boolean", nullable: true),
                    GradientFadeStart = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterPortraitConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterPortraitConfigs_Game_Name",
                table: "CharacterPortraitConfigs",
                columns: new[] { "Game", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterPortraitConfigs");
        }
    }
}
