using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.CharacterDb
{
    /// <inheritdoc />
    public partial class UserPortraitActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "UserPortraitUploads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_UserPortraitUploads_DiscordUserId_Game_CharacterName_IsActi~",
                table: "UserPortraitUploads",
                columns: new[] { "DiscordUserId", "Game", "CharacterName", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPortraitUploads_DiscordUserId_Game_CharacterName_IsActi~",
                table: "UserPortraitUploads");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "UserPortraitUploads");
        }
    }
}
