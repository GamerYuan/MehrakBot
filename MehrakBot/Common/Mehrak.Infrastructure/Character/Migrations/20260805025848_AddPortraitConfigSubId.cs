using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.CharacterDb
{
    /// <inheritdoc />
    public partial class AddPortraitConfigSubId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharacterPortraitConfigs_Game_ServerId",
                table: "CharacterPortraitConfigs");

            migrationBuilder.AddColumn<int>(
                name: "SubId",
                table: "CharacterPortraitConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CharacterPortraitConfigs_Game_ServerId_SubId",
                table: "CharacterPortraitConfigs",
                columns: new[] { "Game", "ServerId", "SubId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CharacterPortraitConfigs_Game_ServerId_SubId",
                table: "CharacterPortraitConfigs");

            migrationBuilder.DropColumn(
                name: "SubId",
                table: "CharacterPortraitConfigs");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterPortraitConfigs_Game_ServerId",
                table: "CharacterPortraitConfigs",
                columns: new[] { "Game", "ServerId" },
                unique: true);
        }
    }
}
