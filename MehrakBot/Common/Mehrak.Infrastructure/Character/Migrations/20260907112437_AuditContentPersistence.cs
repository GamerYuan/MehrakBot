using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mehrak.Infrastructure.Character.Migrations
{
    /// <inheritdoc />
    public partial class AuditContentPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AliasConflicts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Game = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OriginalAlias = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CharacterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceAliasId = table.Column<int>(type: "integer", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AliasConflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPortraitDeletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserPortraitUploadId = table.Column<Guid>(type: "uuid", nullable: true),
                    S3Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPortraitDeletions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPortraitDeletions_UserPortraitUploadId",
                table: "UserPortraitDeletions",
                column: "UserPortraitUploadId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AliasConflicts");

            migrationBuilder.DropTable(
                name: "UserPortraitDeletions");
        }
    }
}
