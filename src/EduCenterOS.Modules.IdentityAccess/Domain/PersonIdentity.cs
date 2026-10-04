namespace EduCenterOS.Modules.IdentityAccess.Domain;

internal sealed class PersonIdentity
{
    private PersonIdentity()
    {
    }

    internal PersonIdentity(Guid id, string fullName, DateTimeOffset now)
    {
        RegistrationErrors.RequireUtc(now);

        var name = RegistrationInputs.Name(fullName);

        if (id == Guid.Empty || !name.IsSuccess || name.Value != fullName) throw new ArgumentException("IdentityAccess.InvalidPerson");

        Id = id;
        FullName = fullName;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
