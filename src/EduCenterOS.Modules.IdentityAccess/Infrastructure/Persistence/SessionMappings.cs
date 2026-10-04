using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal sealed class SessionMapping : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> entity)
    {
        entity.ToTable("user_sessions", table =>
        {
            table.HasCheckConstraint("ck_session_security", "security_version_at_authentication >= 1 AND version >= 1 AND assurance_level = 'PrimaryAuthenticated'");
            table.HasCheckConstraint("ck_session_times", "created_at_utc <= authenticated_at_utc AND authenticated_at_utc <= last_primary_authenticated_at_utc AND last_seen_at_utc >= created_at_utc AND idle_expires_at_utc > created_at_utc AND idle_expires_at_utc <= absolute_expires_at_utc");
            table.HasCheckConstraint("ck_session_revocation", "(revoked_at_utc IS NULL AND revocation_reason IS NULL) OR (revoked_at_utc IS NOT NULL AND revocation_reason IS NOT NULL AND revoked_at_utc >= created_at_utc AND revocation_reason IN ('LogoutCurrent','LogoutAll','UserRequest','RefreshReuse','SecurityReset'))");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).ValueGeneratedNever();
        entity.HasOne<UserAccount>().WithMany().HasForeignKey(value => value.UserAccountId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(value => new { value.UserAccountId, value.CreatedAtUtc, value.Id });
        entity.Property(value => value.AssuranceLevel).HasMaxLength(32).IsRequired();
        entity.Property(value => value.RevocationReason).HasMaxLength(32);
        entity.Property(value => value.Version).IsConcurrencyToken();
        entity.Ignore(value => value.EffectiveExpiration);
    }
}

internal sealed class RefreshMapping : IEntityTypeConfiguration<RefreshTokenRecord>
{
    public void Configure(EntityTypeBuilder<RefreshTokenRecord> entity)
    {
        entity.ToTable("refresh_token_records", table =>
        {
            table.HasCheckConstraint("ck_refresh_security", "octet_length(token_hash) = 32 AND version >= 1");
            table.HasCheckConstraint("ck_refresh_times", "expires_at_utc > created_at_utc AND (consumed_at_utc IS NULL OR consumed_at_utc >= created_at_utc) AND (revoked_at_utc IS NULL OR revoked_at_utc >= created_at_utc)");
            table.HasCheckConstraint("ck_refresh_consumption", "(consumed_at_utc IS NULL AND replaced_by_token_id IS NULL) OR (consumed_at_utc IS NOT NULL AND replaced_by_token_id IS NOT NULL AND replaced_by_token_id <> id)");
        });
        entity.HasKey(value => value.Id);
        entity.Property(value => value.Id).ValueGeneratedNever();
        entity.HasAlternateKey(value => new { value.UserSessionId, value.Id });
        entity.HasOne<UserSession>().WithMany().HasForeignKey(value => value.UserSessionId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<RefreshTokenRecord>().WithMany()
            .HasForeignKey(value => new { value.UserSessionId, value.ReplacedByTokenId })
            .HasPrincipalKey(value => new { value.UserSessionId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(value => value.TokenHash).IsUnique();
        entity.HasIndex(value => value.ReplacedByTokenId).IsUnique().HasFilter("replaced_by_token_id IS NOT NULL");
        entity.Property(value => value.TokenHash).IsRequired();
        entity.Property(value => value.Version).IsConcurrencyToken();
    }
}

internal sealed class LoginTargetMapping : IEntityTypeConfiguration<LoginTarget>
{
    public void Configure(EntityTypeBuilder<LoginTarget> entity)
    {
        entity.ToTable("login_targets", table => table.HasCheckConstraint("ck_login_target_budget", "octet_length(digest) = 32 AND cardinality(attempts_utc) <= 20"));
        entity.HasKey(value => value.Digest);
        entity.Property(value => value.Digest).ValueGeneratedNever();
        entity.Property(value => value.AttemptsUtc).HasColumnType("timestamp with time zone[]").IsRequired();
        entity.HasIndex(value => value.LastAttemptAtUtc);
    }
}
