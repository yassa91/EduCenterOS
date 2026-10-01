using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class BindPersistentRateKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rate_key_binding",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    fingerprint = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_key_binding", x => x.id);
                    table.CheckConstraint("ck_rate_key_binding", "id = 1 AND octet_length(fingerprint) = 32");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rate_key_binding",
                schema: "identity_access");
        }
    }
}
