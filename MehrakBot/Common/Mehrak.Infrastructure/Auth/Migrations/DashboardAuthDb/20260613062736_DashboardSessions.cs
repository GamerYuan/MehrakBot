using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.DashboardAuthDb
{
    /// <inheritdoc />
    public partial class DashboardSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DiscordId = table.Column<long>(type: "bigint", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastTokenValidation = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LoginIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardSessions_DiscordId",
                table: "DashboardSessions",
                column: "DiscordId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardSessions_ExpiresAt",
                table: "DashboardSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardSessions_Token",
                table: "DashboardSessions",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardSessions");
        }
    }
}
