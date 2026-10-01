namespace EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;

internal sealed class OtpMessage(Guid challengeId, string code, DateTimeOffset expiresAtUtc)
{
    public Guid ChallengeId { get; } = challengeId;
    public string Code { get; } = code;
    public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
}
internal interface IOtpSender
{
    Task DeliverAsync(OtpMessage message, CancellationToken cancellationToken);
    Task RemoveAsync(Guid challengeId, CancellationToken cancellationToken);
}
internal sealed class OtpDeliveryException : Exception
{
    internal OtpDeliveryException() : base("Delivery.Unavailable") { }
}
internal sealed class UnavailableOtpSender : IOtpSender
{
    public Task DeliverAsync(OtpMessage message, CancellationToken cancellationToken) => throw new OtpDeliveryException();
    public Task RemoveAsync(Guid challengeId, CancellationToken cancellationToken) => Task.CompletedTask;
}
