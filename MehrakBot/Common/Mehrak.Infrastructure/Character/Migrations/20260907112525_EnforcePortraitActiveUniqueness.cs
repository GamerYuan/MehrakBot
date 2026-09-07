using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Character.Migrations
{
    /// <inheritdoc />
    public partial class EnforcePortraitActiveUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPortraitUploads_DiscordUserId_Game_CharacterName_IsActi~",
                table: "UserPortraitUploads");

            migrationBuilder.CreateIndex(
                name: "IX_UserPortraitUploads_DiscordUserId_Game_CharacterName_IsActi~",
                table: "UserPortraitUploads",
                columns: new[] { "DiscordUserId", "Game", "CharacterName", "IsActive" },
                unique: true,
                filter: "\"IsActive\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPortraitUploads_DiscordUserId_Game_CharacterName_IsActi~",
                table: "UserPortraitUploads");

            migrationBuilder.CreateIndex(
                name: "IX_UserPortraitUploads_DiscordUserId_Game_CharacterName_IsActi~",
                table: "UserPortraitUploads",
                columns: new[] { "DiscordUserId", "Game", "CharacterName", "IsActive" });
        }
    }
}
