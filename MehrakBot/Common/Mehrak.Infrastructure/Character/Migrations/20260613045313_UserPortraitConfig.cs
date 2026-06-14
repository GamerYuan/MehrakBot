using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.CharacterDb
{
    /// <inheritdoc />
    public partial class UserPortraitConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPortraitUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscordUserId = table.Column<long>(type: "bigint", nullable: false),
                    Game = table.Column<int>(type: "integer", nullable: false),
                    CharacterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SHA256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    S3Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPortraitUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPortraitConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserPortraitUploadId = table.Column<Guid>(type: "uuid", nullable: false),
                    OffsetX = table.Column<int>(type: "integer", nullable: true),
                    OffsetY = table.Column<int>(type: "integer", nullable: true),
                    TargetScale = table.Column<float>(type: "real", nullable: true),
                    EnableGradientFade = table.Column<bool>(type: "boolean", nullable: true),
                    GradientFadeStart = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPortraitConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPortraitConfigs_UserPortraitUploads_UserPortraitUploadId",
                        column: x => x.UserPortraitUploadId,
                        principalTable: "UserPortraitUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPortraitConfigs_UserPortraitUploadId",
                table: "UserPortraitConfigs",
                column: "UserPortraitUploadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPortraitUploads_DiscordUserId_Game_CharacterName",
                table: "UserPortraitUploads",
                columns: new[] { "DiscordUserId", "Game", "CharacterName" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPortraitUploads_DiscordUserId_Game_CharacterName_SHA256~",
                table: "UserPortraitUploads",
                columns: new[] { "DiscordUserId", "Game", "CharacterName", "SHA256Hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPortraitConfigs");

            migrationBuilder.DropTable(
                name: "UserPortraitUploads");
        }
    }
}
