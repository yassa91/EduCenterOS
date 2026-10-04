using System.Buffers.Binary;
using EduCenterOS.BuildingBlocks.Time;
using EduCenterOS.Modules.IdentityAccess.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Security;

internal sealed class RegistrationStateCleanup(IdentityAccessRuntimeSettings settings, IClock clock, ILogger<RegistrationStateCleanup> logger) : BackgroundService
{
    internal async Task SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var cutoff = now.AddSeconds(-settings.Policy.RollingWindowSeconds - Math.Max(settings.Policy.CodeLifetimeSeconds, settings.Policy.ProofLifetimeSeconds));
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var candidates = new List<byte[]>();

        await using (var query = new NpgsqlCommand(
            """
            SELECT digest FROM identity_access.verification_targets
            WHERE (last_issued_at_utc IS NULL OR last_issued_at_utc <= @cutoff)
              AND NOT EXISTS (SELECT 1 FROM unnest(verifications_utc) AS t WHERE t > @cutoff) LIMIT 100
            """,
            connection,
            transaction
        ))
        {
            query.Parameters.AddWithValue("cutoff", cutoff);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken)) candidates.Add(reader.GetFieldValue<byte[]>(0));
        }

        foreach (var digest in candidates)
        {
            await using var claim = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@lock)", connection, transaction);
            claim.Parameters.AddWithValue("lock", BinaryPrimitives.ReadInt64BigEndian(digest));

            if (await claim.ExecuteScalarAsync(cancellationToken) is not true) continue;

            await using var prune = new NpgsqlCommand(
                """
                DELETE FROM identity_access.verification_targets WHERE digest=@digest
                  AND (last_issued_at_utc IS NULL OR last_issued_at_utc <= @cutoff)
                  AND NOT EXISTS (SELECT 1 FROM unnest(verifications_utc) AS t WHERE t > @cutoff)
                """,
                connection,
                transaction
            );
            prune.Parameters.AddWithValue("digest", digest);
            prune.Parameters.AddWithValue("cutoff", cutoff);
            await prune.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var challenges = new NpgsqlCommand(
            """
            DELETE FROM identity_access.otp_challenges WHERE id IN
              (SELECT id FROM identity_access.otp_challenges WHERE created_at_utc <= @cutoff
                 AND expires_at_utc <= @now AND (proof_expires_at_utc IS NULL OR proof_expires_at_utc <= @now) LIMIT 100)
            """,
            connection,
            transaction
        );
        challenges.Parameters.AddWithValue("cutoff", cutoff);
        challenges.Parameters.AddWithValue("now", now);
        await challenges.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                logger.LogWarning(new EventId(1100, "RegistrationRetentionUnavailable"), "Registration security retention is temporarily unavailable.");
            }
        }
    }
}
