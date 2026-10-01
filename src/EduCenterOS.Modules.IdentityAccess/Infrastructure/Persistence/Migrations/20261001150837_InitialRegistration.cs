using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    internal partial class InitialRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity_access");

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    normalized_target = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash_key_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    proof_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    proof_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_challenges", x => x.id);
                    table.CheckConstraint("ck_challenge_binding", "normalized_target ~ '^[+]201[0125][0-9]{8}$' AND octet_length(code_hash) = 32 AND length(hash_key_version) BETWEEN 1 AND 32");
                    table.CheckConstraint("ck_challenge_state", "status IN (0,1,2,3,4) AND failed_attempts BETWEEN 0 AND 10 AND purpose = 'RegisterAccount'");
                    table.CheckConstraint("ck_challenge_times", "expires_at_utc > created_at_utc AND (status <> 1 OR (proof_hash IS NOT NULL AND octet_length(proof_hash) = 32 AND verified_at_utc IS NOT NULL AND proof_expires_at_utc IS NOT NULL AND proof_expires_at_utc > verified_at_utc)) AND (status <> 0 OR proof_hash IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "person_identities",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_identities", x => x.id);
                    table.CheckConstraint("ck_person_name", "length(full_name) BETWEEN 2 AND 200");
                });

            migrationBuilder.CreateTable(
                name: "verification_targets",
                schema: "identity_access",
                columns: table => new
                {
                    digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    issues_utc = table.Column<DateTimeOffset[]>(type: "timestamp with time zone[]", nullable: false),
                    verifications_utc = table.Column<DateTimeOffset[]>(type: "timestamp with time zone[]", nullable: false),
                    last_issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verification_targets", x => x.digest);
                    table.CheckConstraint("ck_target_budget", "octet_length(digest) = 32 AND cardinality(issues_utc) <= 3 AND cardinality(verifications_utc) <= 10");
                });

            migrationBuilder.CreateTable(
                name: "user_accounts",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    normalized_phone_number = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    phone_verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    email_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    normalized_email_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    email_verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    password_hash = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    password_changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    initial_onboarding_intent = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    security_version = table.Column<long>(type: "bigint", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_accounts", x => x.id);
                    table.CheckConstraint("ck_account_email", "email_address IS NOT DISTINCT FROM normalized_email_address AND (normalized_email_address IS NULL OR normalized_email_address = lower(normalized_email_address))");
                    table.CheckConstraint("ck_account_phone", "normalized_phone_number ~ '^[+]201[0125][0-9]{8}$' AND phone_number = normalized_phone_number");
                    table.CheckConstraint("ck_account_security", "status IN (0,1,2) AND security_version >= 1 AND version >= 1 AND access_failed_count >= 0");
                    table.CheckConstraint("ck_account_times", "phone_verified_at_utc <= created_at_utc AND password_changed_at_utc >= created_at_utc");
                    table.ForeignKey(
                        name: "FK_user_accounts_person_identities_person_identity_id",
                        column: x => x.person_identity_id,
                        principalSchema: "identity_access",
                        principalTable: "person_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_created_at_utc",
                schema: "identity_access",
                table: "otp_challenges",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_otp_challenges_purpose_normalized_target",
                schema: "identity_access",
                table: "otp_challenges",
                columns: new[] { "purpose", "normalized_target" },
                unique: true,
                filter: "status IN (0,1)");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_normalized_email_address",
                schema: "identity_access",
                table: "user_accounts",
                column: "normalized_email_address",
                unique: true,
                filter: "normalized_email_address IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_normalized_phone_number",
                schema: "identity_access",
                table: "user_accounts",
                column: "normalized_phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_person_identity_id",
                schema: "identity_access",
                table: "user_accounts",
                column: "person_identity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_verification_targets_last_issued_at_utc",
                schema: "identity_access",
                table: "verification_targets",
                column: "last_issued_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "user_accounts",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "verification_targets",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "person_identities",
                schema: "identity_access");
        }
    }
}
