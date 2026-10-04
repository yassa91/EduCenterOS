using System.Collections.Concurrent;
using EduCenterOS.Modules.IdentityAccess.Infrastructure.Delivery;

namespace EduCenterOS.IntegrationTests.IdentityAccess;

internal sealed class TestOtpSender : IOtpSender
{
    private readonly ConcurrentDictionary<Guid, OtpMessage> messages = new();
    internal bool RejectDeliveries { get; set; }

    internal OtpMessage Read(Guid id) => messages[id];

    internal bool Contains(Guid id) => messages.ContainsKey(id);

    public Task DeliverAsync(OtpMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (RejectDeliveries) throw new OtpDeliveryException();

        if (!messages.TryAdd(message.ChallengeId, message)) throw new InvalidOperationException("TestSender.DuplicateDelivery");

        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        messages.TryRemove(id, out _);

        return Task.CompletedTask;
    }
}
