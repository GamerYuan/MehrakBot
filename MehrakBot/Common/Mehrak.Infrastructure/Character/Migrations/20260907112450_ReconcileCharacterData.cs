using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mehrak.Infrastructure.Character.Migrations;

/// <summary>
/// Hand-authored data migration. It runs after the generated persistence tables
/// exist and before the generated filtered active-portrait index is applied.
/// </summary>
public partial class ReconcileCharacterData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Aliases_Game_Alias\";");

        migrationBuilder.Sql("""
            WITH ranked AS
            (
                SELECT "Id", "Game", "Alias" AS "OriginalAlias", "CharacterName",
                       lower(trim(replace(replace("Alias", E'\r', ''), E'\n', ''))) AS "NormalizedAlias",
                       row_number() OVER (
                           PARTITION BY "Game", lower(trim(replace(replace("Alias", E'\r', ''), E'\n', '')))
                           ORDER BY "Id") AS row_number,
                       first_value("CharacterName") OVER (
                           PARTITION BY "Game", lower(trim(replace(replace("Alias", E'\r', ''), E'\n', '')))
                           ORDER BY "Id") AS winner
                FROM "Aliases"
            )
            INSERT INTO "AliasConflicts" ("Game", "Alias", "OriginalAlias", "CharacterName", "SourceAliasId", "RecordedAtUtc")
            SELECT "Game", "NormalizedAlias", "OriginalAlias", "CharacterName", "Id", now()
            FROM ranked
            WHERE row_number > 1 AND lower("CharacterName") <> lower(winner);

            UPDATE "Aliases"
            SET "Alias" = lower(trim(replace(replace("Alias", E'\r', ''), E'\n', '')));

            WITH ranked AS
            (
                SELECT "Id",
                       row_number() OVER (PARTITION BY "Game", "Alias" ORDER BY "Id") AS row_number
                FROM "Aliases"
            )
            DELETE FROM "Aliases" AS aliases
            USING ranked
            WHERE aliases."Id" = ranked."Id" AND ranked.row_number > 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Aliases_Game_Alias",
            table: "Aliases",
            columns: new[] { "Game", "Alias" },
            unique: true);

        migrationBuilder.Sql("""
            WITH ranked AS
            (
                SELECT "Id",
                       row_number() OVER (
                           PARTITION BY "DiscordUserId", "Game", "CharacterName"
                           ORDER BY "CreatedAt", "Id") AS row_number
                FROM "UserPortraitUploads"
                WHERE "IsActive" = TRUE
            )
            UPDATE "UserPortraitUploads" AS uploads
            SET "IsActive" = FALSE
            FROM ranked
            WHERE uploads."Id" = ranked."Id" AND ranked.row_number > 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The canonicalization and conflict ledger are intentionally not
        // reversed: the migration preserves all conflicting source data in the
        // ledger, but cannot safely restore an invalid active-state invariant.
    }
}
