using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Persistence;

internal sealed class RateKeyBinding
{
    private RateKeyBinding() { }
    internal RateKeyBinding(byte[] fingerprint) => Fingerprint = fingerprint.ToArray();
    public int Id { get; private set; } = 1;
    public byte[] Fingerprint { get; private set; } = [];
}
internal sealed class RateKeyMapping : IEntityTypeConfiguration<RateKeyBinding>
{
    public void Configure(EntityTypeBuilder<RateKeyBinding> entity)
    {
        entity.ToTable("rate_key_binding", table => table.HasCheckConstraint("ck_rate_key_binding", "id = 1 AND octet_length(fingerprint) = 32"));
        entity.HasKey(value => value.Id); entity.Property(value => value.Id).ValueGeneratedNever();
    }
}
