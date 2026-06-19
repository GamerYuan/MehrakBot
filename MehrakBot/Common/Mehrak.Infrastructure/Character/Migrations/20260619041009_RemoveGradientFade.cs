using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Migrations.CharacterDb
{
    /// <inheritdoc />
    public partial class RemoveGradientFade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableGradientFade",
                table: "UserPortraitConfigs");

            migrationBuilder.DropColumn(
                name: "GradientFadeStart",
                table: "UserPortraitConfigs");

            migrationBuilder.DropColumn(
                name: "EnableGradientFade",
                table: "CharacterPortraitConfigs");

            migrationBuilder.DropColumn(
                name: "GradientFadeStart",
                table: "CharacterPortraitConfigs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableGradientFade",
                table: "UserPortraitConfigs",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "GradientFadeStart",
                table: "UserPortraitConfigs",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableGradientFade",
                table: "CharacterPortraitConfigs",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "GradientFadeStart",
                table: "CharacterPortraitConfigs",
                type: "real",
                nullable: true);
        }
    }
}
