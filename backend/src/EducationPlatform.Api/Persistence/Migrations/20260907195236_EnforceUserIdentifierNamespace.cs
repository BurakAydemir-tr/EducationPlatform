using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationPlatform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUserIdentifierNamespace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE "UserIdentifiers" (
                    "Value" character varying(256) NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Kind" character varying(16) NOT NULL,
                    CONSTRAINT "PK_UserIdentifiers" PRIMARY KEY ("Value"),
                    CONSTRAINT "FK_UserIdentifiers_AspNetUsers_UserId"
                        FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
                );

                CREATE INDEX "IX_UserIdentifiers_UserId" ON "UserIdentifiers" ("UserId");

                INSERT INTO "UserIdentifiers" ("Value", "UserId", "Kind")
                SELECT "NormalizedUserName", "Id", 'UserName'
                FROM "AspNetUsers"
                WHERE "NormalizedUserName" IS NOT NULL
                UNION ALL
                SELECT upper(btrim("StudentCode")), "Id", 'StudentCode'
                FROM "AspNetUsers"
                WHERE "StudentCode" IS NOT NULL AND btrim("StudentCode") <> '';

                CREATE FUNCTION sync_user_identifiers()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    DELETE FROM "UserIdentifiers" WHERE "UserId" = NEW."Id";

                    IF NEW."NormalizedUserName" IS NOT NULL THEN
                        INSERT INTO "UserIdentifiers" ("Value", "UserId", "Kind")
                        VALUES (NEW."NormalizedUserName", NEW."Id", 'UserName');
                    END IF;

                    IF NEW."StudentCode" IS NOT NULL AND btrim(NEW."StudentCode") <> '' THEN
                        INSERT INTO "UserIdentifiers" ("Value", "UserId", "Kind")
                        VALUES (upper(btrim(NEW."StudentCode")), NEW."Id", 'StudentCode');
                    END IF;

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER "TR_AspNetUsers_SyncIdentifiers"
                AFTER INSERT OR UPDATE OF "NormalizedUserName", "StudentCode"
                ON "AspNetUsers"
                FOR EACH ROW
                EXECUTE FUNCTION sync_user_identifiers();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_AspNetUsers_SyncIdentifiers" ON "AspNetUsers";
                DROP FUNCTION IF EXISTS sync_user_identifiers();
                DROP TABLE IF EXISTS "UserIdentifiers";
                """);
        }
    }
}
