using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class AuthenticationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_targets",
                schema: "identity_access",
                columns: table => new
                {
                    digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    attempts_utc = table.Column<DateTimeOffset[]>(type: "timestamp with time zone[]", nullable: false),
                    last_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_targets", x => x.digest);
                    table.CheckConstraint("ck_login_target_budget", "octet_length(digest) = 32 AND cardinality(attempts_utc) <= 20");
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_version_at_authentication = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authenticated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_primary_authenticated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idle_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocation_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    assurance_level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.id);
                    table.CheckConstraint("ck_session_revocation", "(revoked_at_utc IS NULL AND revocation_reason IS NULL) OR (revoked_at_utc IS NOT NULL AND revocation_reason IS NOT NULL AND revoked_at_utc >= created_at_utc AND revocation_reason IN ('LogoutCurrent','LogoutAll','UserRequest','RefreshReuse','SecurityReset'))");
                    table.CheckConstraint("ck_session_security", "security_version_at_authentication >= 1 AND version >= 1 AND assurance_level = 'PrimaryAuthenticated'");
                    table.CheckConstraint("ck_session_times", "created_at_utc <= authenticated_at_utc AND authenticated_at_utc <= last_primary_authenticated_at_utc AND last_seen_at_utc >= created_at_utc AND idle_expires_at_utc > created_at_utc AND idle_expires_at_utc <= absolute_expires_at_utc");
                    table.ForeignKey(
                        name: "FK_user_sessions_user_accounts_user_account_id",
                        column: x => x.user_account_id,
                        principalSchema: "identity_access",
                        principalTable: "user_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token_records",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token_records", x => x.id);
                    table.UniqueConstraint("AK_refresh_token_records_user_session_id_id", x => new { x.user_session_id, x.id });
                    table.CheckConstraint("ck_refresh_consumption", "(consumed_at_utc IS NULL AND replaced_by_token_id IS NULL) OR (consumed_at_utc IS NOT NULL AND replaced_by_token_id IS NOT NULL AND replaced_by_token_id <> id)");
                    table.CheckConstraint("ck_refresh_security", "octet_length(token_hash) = 32 AND version >= 1");
                    table.CheckConstraint("ck_refresh_times", "expires_at_utc > created_at_utc AND (consumed_at_utc IS NULL OR consumed_at_utc >= created_at_utc) AND (revoked_at_utc IS NULL OR revoked_at_utc >= created_at_utc)");
                    table.ForeignKey(
                        name: "FK_refresh_token_records_refresh_token_records_user_session_id~",
                        columns: x => new { x.user_session_id, x.replaced_by_token_id },
                        principalSchema: "identity_access",
                        principalTable: "refresh_token_records",
                        principalColumns: new[] { "user_session_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refresh_token_records_user_sessions_user_session_id",
                        column: x => x.user_session_id,
                        principalSchema: "identity_access",
                        principalTable: "user_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_login_targets_last_attempt_at_utc",
                schema: "identity_access",
                table: "login_targets",
                column: "last_attempt_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_records_replaced_by_token_id",
                schema: "identity_access",
                table: "refresh_token_records",
                column: "replaced_by_token_id",
                unique: true,
                filter: "replaced_by_token_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_records_token_hash",
                schema: "identity_access",
                table: "refresh_token_records",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_records_user_session_id_replaced_by_token_id",
                schema: "identity_access",
                table: "refresh_token_records",
                columns: new[] { "user_session_id", "replaced_by_token_id" });

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_user_account_id_created_at_utc_id",
                schema: "identity_access",
                table: "user_sessions",
                columns: new[] { "user_account_id", "created_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_targets",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "refresh_token_records",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "identity_access");
        }
    }
}
