using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.DashboardAuthDb
{
    /// <inheritdoc />
    public partial class Dashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSuperAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DashboardGamePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AllowWrite = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardGamePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardGamePermissions_DashboardUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "DashboardUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DashboardSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardSessions_DashboardUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "DashboardUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardGamePermissions_UserId_GameCode",
                table: "DashboardGamePermissions",
                columns: new[] { "UserId", "GameCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardSessions_SessionToken",
                table: "DashboardSessions",
                column: "SessionToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardSessions_UserId",
                table: "DashboardSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardUsers_DiscordId",
                table: "DashboardUsers",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DashboardUsers_Username",
                table: "DashboardUsers",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardGamePermissions");

            migrationBuilder.DropTable(
                name: "DashboardSessions");

            migrationBuilder.DropTable(
                name: "DashboardUsers");
        }
    }
}
