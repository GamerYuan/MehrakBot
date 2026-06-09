using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.CharacterDb
{
    /// <inheritdoc />
    public partial class PortraitConfigServerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharacterPortraitConfigs_Game_Name",
                table: "CharacterPortraitConfigs");

            migrationBuilder.AddColumn<int>(
                name: "ServerId",
                table: "CharacterPortraitConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CharacterPortraitConfigs_Game_ServerId",
                table: "CharacterPortraitConfigs",
                columns: new[] { "Game", "ServerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharacterPortraitConfigs_Game_ServerId",
                table: "CharacterPortraitConfigs");

            migrationBuilder.DropColumn(
                name: "ServerId",
                table: "CharacterPortraitConfigs");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterPortraitConfigs_Game_Name",
                table: "CharacterPortraitConfigs",
                columns: new[] { "Game", "Name" },
                unique: true);
        }
    }
}
