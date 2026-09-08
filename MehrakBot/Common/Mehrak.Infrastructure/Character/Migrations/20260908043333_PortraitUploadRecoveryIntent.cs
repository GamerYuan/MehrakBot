using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Character.Migrations
{
    /// <inheritdoc />
    public partial class PortraitUploadRecoveryIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPortraitUploadIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscordUserId = table.Column<long>(type: "bigint", nullable: false),
                    Game = table.Column<int>(type: "integer", nullable: false),
                    CharacterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SHA256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    S3Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPortraitUploadIntents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPortraitUploadIntents_DiscordUserId_Game_CharacterName_~",
                table: "UserPortraitUploadIntents",
                columns: new[] { "DiscordUserId", "Game", "CharacterName", "SHA256Hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPortraitUploadIntents");
        }
    }
}
