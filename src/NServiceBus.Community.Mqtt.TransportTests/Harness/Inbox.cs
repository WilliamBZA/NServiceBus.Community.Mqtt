#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using NServiceBus.Transport;

/// <summary>Collects the messages an endpoint handled, so a test can wait for them and look at what arrived.</summary>
sealed class Inbox
{
    public int Count
    {
        get
        {
            lock (items)
            {
                return items.Count;
            }
        }
    }

    public Received[] Items
    {
        get
        {
            lock (items)
            {
                return items.ToArray();
            }
        }
    }

    public Task Handle(MessageContext context, CancellationToken cancellationToken = default)
    {
        lock (items)
        {
            items.Add(new Received(context.NativeMessageId, new Dictionary<string, string>(context.Headers), context.Body.ToArray()));
        }

        return Task.CompletedTask;
    }

    public Task WaitFor(int count, string description) => Wait.Until(() => Count >= count, description);

    /// <summary>Waits a moment and then checks that nothing more has arrived. A negative can only be shown by waiting.</summary>
    public async Task AssertCount(int expected, string description, TimeSpan? settle = null)
    {
        await Task.Delay(settle ?? TimeSpan.FromMilliseconds(750));

        Assert.That(Count, Is.EqualTo(expected), description);
    }

    readonly List<Received> items = [];

    public sealed record Received(string NativeMessageId, Dictionary<string, string> Headers, byte[] Body);
}
