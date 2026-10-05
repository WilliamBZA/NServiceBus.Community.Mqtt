namespace NServiceBus.Community.Mqtt.Tests;

using NServiceBus.Performance.TimeToBeReceived;
using NServiceBus.Transport.Mqtt;

[TestFixture]
public class MessageExpiryTests
{
    [Test]
    public void Should_not_expire_a_message_without_a_time_to_be_received()
    {
        Assert.That(MqttDispatcher.ToMessageExpiry(null), Is.Null);
    }

    [TestCase(1000, 1u)]
    [TestCase(1500, 2u)]
    [TestCase(2000, 2u)]
    [TestCase(60_000, 60u)]
    public void Should_use_whole_seconds_rounded_up(int milliseconds, uint expected)
    {
        Assert.That(MqttDispatcher.ToMessageExpiry(new DiscardIfNotReceivedBefore(TimeSpan.FromMilliseconds(milliseconds))), Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(100)]
    [TestCase(999)]
    public void Should_never_use_zero_seconds_which_would_expire_the_message_at_once(int milliseconds)
    {
        Assert.That(MqttDispatcher.ToMessageExpiry(new DiscardIfNotReceivedBefore(TimeSpan.FromMilliseconds(milliseconds))), Is.EqualTo(1u));
    }

    [Test]
    public void Should_not_expire_a_message_whose_time_to_be_received_is_longer_than_mqtt_can_express()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MqttDispatcher.ToMessageExpiry(new DiscardIfNotReceivedBefore(TimeSpan.MaxValue)), Is.Null, "TimeSpan.MaxValue means no limit");
            Assert.That(MqttDispatcher.ToMessageExpiry(new DiscardIfNotReceivedBefore(TimeSpan.FromSeconds(uint.MaxValue))), Is.Null);
        });
    }

    [Test]
    public void Should_keep_the_longest_interval_mqtt_can_express()
    {
        Assert.That(MqttDispatcher.ToMessageExpiry(new DiscardIfNotReceivedBefore(TimeSpan.FromSeconds(uint.MaxValue - 1))), Is.EqualTo(uint.MaxValue - 1));
    }
}
