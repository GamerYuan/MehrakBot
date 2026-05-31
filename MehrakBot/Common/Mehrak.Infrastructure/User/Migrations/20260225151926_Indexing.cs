using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Indexing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_UserId",
                table: "UserProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Id",
                table: "Users",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_LtUid",
                table: "UserProfiles",
                column: "LtUid");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UserId_ProfileId",
                table: "UserProfiles",
                columns: new[] { "UserId", "ProfileId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Id",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_LtUid",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_UserId_ProfileId",
                table: "UserProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UserId",
                table: "UserProfiles",
                column: "UserId");
        }
    }
}
