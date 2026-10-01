using EduCenterOS.Modules.IdentityAccess.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal sealed class PersonMapping : IEntityTypeConfiguration<PersonIdentity>
{
    public void Configure(EntityTypeBuilder<PersonIdentity> entity)
    {
        entity.ToTable("person_identities", table => table.HasCheckConstraint("ck_person_name", "(length(full_name) + length(regexp_replace(full_name, U&'[^\\+010000-\\+10FFFF]', '', 'g'))) BETWEEN 2 AND 200"));
        entity.HasKey(value => value.Id); entity.Property(value => value.Id).ValueGeneratedNever();
        entity.Property(value => value.FullName).HasMaxLength(200).IsRequired();
    }
}
internal sealed class AccountMapping : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> entity)
    {
        entity.ToTable("user_accounts", table =>
        {
            table.HasCheckConstraint("ck_account_security", "status IN (0,1,2) AND security_version >= 1 AND version >= 1 AND access_failed_count >= 0");
            table.HasCheckConstraint("ck_account_phone", "normalized_phone_number ~ '^[+]201[0125][0-9]{8}$' AND phone_number = normalized_phone_number");
            table.HasCheckConstraint("ck_account_email", "email_address IS NOT DISTINCT FROM normalized_email_address AND (normalized_email_address IS NULL OR normalized_email_address = lower(normalized_email_address))");
            table.HasCheckConstraint("ck_account_times", "phone_verified_at_utc <= created_at_utc AND password_changed_at_utc >= created_at_utc");
        });
        entity.HasKey(value => value.Id); entity.Property(value => value.Id).ValueGeneratedNever();
        entity.HasOne<PersonIdentity>().WithOne().HasForeignKey<UserAccount>(value => value.PersonIdentityId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(value => value.PersonIdentityId).IsUnique();
        entity.HasIndex(value => value.NormalizedPhoneNumber).IsUnique();
        entity.HasIndex(value => value.NormalizedEmailAddress).IsUnique().HasFilter("normalized_email_address IS NOT NULL");
        entity.Property(value => value.PhoneNumber).HasMaxLength(13).IsRequired();
        entity.Property(value => value.NormalizedPhoneNumber).HasMaxLength(13).IsRequired();
        entity.Property(value => value.EmailAddress).HasMaxLength(254); entity.Property(value => value.NormalizedEmailAddress).HasMaxLength(254);
        entity.Property(value => value.PasswordHash).HasMaxLength(1024).IsRequired();
        entity.Property(value => value.InitialOnboardingIntent).HasMaxLength(32);
        entity.Property(value => value.Version).IsConcurrencyToken();
    }
}

internal sealed class ChallengeMapping : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> entity)
    {
        entity.ToTable("otp_challenges", table =>
        {
            table.HasCheckConstraint("ck_challenge_state", "status IN (0,1,2,3,4) AND failed_attempts BETWEEN 0 AND 10 AND purpose = 'RegisterAccount'");
            table.HasCheckConstraint("ck_challenge_binding", "normalized_target ~ '^[+]201[0125][0-9]{8}$' AND octet_length(code_hash) = 32 AND length(hash_key_version) BETWEEN 1 AND 32");
            table.HasCheckConstraint("ck_challenge_times", "expires_at_utc > created_at_utc AND (status <> 1 OR (proof_hash IS NOT NULL AND octet_length(proof_hash) = 32 AND verified_at_utc IS NOT NULL AND proof_expires_at_utc IS NOT NULL AND proof_expires_at_utc > verified_at_utc)) AND (status <> 0 OR proof_hash IS NULL)");
        });
        entity.HasKey(value => value.Id); entity.Property(value => value.Id).ValueGeneratedNever();
        entity.Property(value => value.Purpose).HasMaxLength(32).IsRequired();
        entity.Property(value => value.NormalizedTarget).HasMaxLength(13).IsRequired();
        entity.Property(value => value.HashKeyVersion).HasMaxLength(32).IsRequired();
        entity.Property(value => value.CodeHash).IsRequired();
        entity.HasIndex(value => new { value.Purpose, value.NormalizedTarget }).IsUnique().HasFilter("status IN (0,1)");
        entity.HasIndex(value => value.CreatedAtUtc);
    }
}

internal sealed class TargetMapping : IEntityTypeConfiguration<VerificationTarget>
{
    public void Configure(EntityTypeBuilder<VerificationTarget> entity)
    {
        entity.ToTable("verification_targets", table => table.HasCheckConstraint("ck_target_budget", "octet_length(digest) = 32 AND cardinality(issues_utc) <= 3 AND cardinality(verifications_utc) <= 10"));
        entity.HasKey(value => value.Digest); entity.Property(value => value.Digest).ValueGeneratedNever();
        entity.Property(value => value.IssuesUtc).HasColumnType("timestamp with time zone[]").IsRequired();
        entity.Property(value => value.VerificationsUtc).HasColumnType("timestamp with time zone[]").IsRequired();
        entity.HasIndex(value => value.LastIssuedAtUtc);
    }
}
