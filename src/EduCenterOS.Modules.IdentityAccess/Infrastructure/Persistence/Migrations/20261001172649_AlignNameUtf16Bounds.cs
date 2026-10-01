using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class AlignNameUtf16Bounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_person_name",
                schema: "identity_access",
                table: "person_identities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_person_name",
                schema: "identity_access",
                table: "person_identities",
                sql: "(length(full_name) + length(regexp_replace(full_name, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) BETWEEN 2 AND 200");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_person_name",
                schema: "identity_access",
                table: "person_identities");

            migrationBuilder.AddCheckConstraint(
                name: "ck_person_name",
                schema: "identity_access",
                table: "person_identities",
                sql: "length(full_name) BETWEEN 2 AND 200");
        }
    }
}
