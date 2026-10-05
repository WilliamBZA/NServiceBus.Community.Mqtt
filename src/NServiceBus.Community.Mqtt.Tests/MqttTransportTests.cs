namespace NServiceBus.Community.Mqtt.Tests;

using NServiceBus.Transport;

[TestFixture]
public class MqttTransportTests
{
    [Test]
    public void Should_be_constructible_without_a_broker()
    {
        var transport = new MqttTransport("localhost", 1883);

        Assert.Multiple(() =>
        {
            Assert.That(transport.Server, Is.EqualTo("localhost"));
            Assert.That(transport.Port, Is.EqualTo(1883));
        });
    }

    [Test]
    public void Should_default_to_the_standard_mqtt_port()
    {
        var transport = new MqttTransport("localhost");

        Assert.That(transport.Port, Is.EqualTo(1883));
    }

    [Test]
    public void Should_default_to_receive_only_transaction_mode()
    {
        var transport = new MqttTransport("localhost");

        Assert.That(transport.TransportTransactionMode, Is.EqualTo(TransportTransactionMode.ReceiveOnly));
    }

    [Test]
    public void Should_support_only_the_none_and_receive_only_transaction_modes()
    {
        var transport = new MqttTransport("localhost");

        Assert.That(
            transport.GetSupportedTransactionModes(),
            Is.EquivalentTo(new[] { TransportTransactionMode.None, TransportTransactionMode.ReceiveOnly }));
    }

    [Test]
    public void Should_not_support_atomic_send_with_receive_or_transaction_scope()
    {
        var transport = new MqttTransport("localhost");

        Assert.That(
            transport.GetSupportedTransactionModes(),
            Has.None.EqualTo(TransportTransactionMode.SendsAtomicWithReceive).Or.EqualTo(TransportTransactionMode.TransactionScope));
    }

    [Test]
    public void Should_have_a_default_transaction_mode_that_it_supports()
    {
        var transport = new MqttTransport("localhost");

        Assert.That(transport.GetSupportedTransactionModes(), Does.Contain(transport.TransportTransactionMode));
    }

    [TestCase(TransportTransactionMode.None)]
    [TestCase(TransportTransactionMode.ReceiveOnly)]
    public void Should_accept_a_supported_transaction_mode(TransportTransactionMode mode)
    {
        var transport = new MqttTransport("localhost") { TransportTransactionMode = mode };

        Assert.That(transport.TransportTransactionMode, Is.EqualTo(mode));
    }

    [TestCase(TransportTransactionMode.TransactionScope)]
    [TestCase(TransportTransactionMode.SendsAtomicWithReceive)]
    public void Should_reject_an_unsupported_transaction_mode_with_the_standard_nservicebus_error(TransportTransactionMode mode)
    {
        var transport = new MqttTransport("localhost");

        // NServiceBus checks the mode against GetSupportedTransactionModes() as soon as it is set, so it never gets as far as endpoint startup
        var exception = Assert.Throws<Exception>(() => transport.TransportTransactionMode = mode);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain(mode.ToString()).And.Contain("not supported"));
            Assert.That(transport.TransportTransactionMode, Is.EqualTo(TransportTransactionMode.ReceiveOnly), "a rejected mode must not change the setting");
        });
    }

    [Test]
    public void Should_declare_native_publish_subscribe_and_ttbr_and_no_delayed_delivery()
    {
        var transport = new MqttTransport("localhost");

        Assert.Multiple(() =>
        {
            Assert.That(transport.SupportsPublishSubscribe, Is.True);
            Assert.That(transport.SupportsDelayedDelivery, Is.False);
            Assert.That(transport.SupportsTTBR, Is.True);
        });
    }

    [Test]
    public void Should_reject_a_null_server()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new MqttTransport(null!));

        Assert.That(exception!.ParamName, Is.EqualTo("server"));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("\t\r\n")]
    public void Should_reject_an_empty_or_whitespace_server(string server)
    {
        var exception = Assert.Throws<ArgumentException>(() => new MqttTransport(server));

        Assert.That(exception!.ParamName, Is.EqualTo("server"));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(65536)]
    [TestCase(70000)]
    public void Should_reject_a_port_outside_the_valid_range(int port)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new MqttTransport("localhost", port));

        Assert.That(exception!.ParamName, Is.EqualTo("port"));
    }

    [TestCase(1)]
    [TestCase(1883)]
    [TestCase(65535)]
    public void Should_accept_ports_at_and_within_the_bounds(int port)
    {
        var transport = new MqttTransport("localhost", port);

        Assert.That(transport.Port, Is.EqualTo(port));
    }

    [Test]
    public void Should_default_the_session_expiry_to_seven_days()
    {
        var transport = new MqttTransport("localhost");

        Assert.That(transport.SessionExpiry, Is.EqualTo(TimeSpan.FromDays(7)));
    }

    [Test]
    public void Should_accept_a_valid_session_expiry()
    {
        var transport = new MqttTransport("localhost") { SessionExpiry = TimeSpan.FromMinutes(5) };

        Assert.That(transport.SessionExpiry, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void Should_accept_the_session_expiry_bounds()
    {
        var transport = new MqttTransport("localhost");

        Assert.Multiple(() =>
        {
            transport.SessionExpiry = TimeSpan.FromSeconds(1);
            Assert.That(transport.SessionExpiry, Is.EqualTo(TimeSpan.FromSeconds(1)));

            transport.SessionExpiry = TimeSpan.FromSeconds(uint.MaxValue);
            Assert.That(transport.SessionExpiry, Is.EqualTo(TimeSpan.FromSeconds(uint.MaxValue)));
        });
    }

    [Test]
    public void Should_reject_a_session_expiry_outside_the_valid_range()
    {
        var transport = new MqttTransport("localhost");

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => transport.SessionExpiry = TimeSpan.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => transport.SessionExpiry = TimeSpan.FromMilliseconds(999));
            Assert.Throws<ArgumentOutOfRangeException>(() => transport.SessionExpiry = TimeSpan.FromSeconds(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => transport.SessionExpiry = TimeSpan.FromSeconds(uint.MaxValue + 1d));
            Assert.That(transport.SessionExpiry, Is.EqualTo(TimeSpan.FromDays(7)), "a rejected value must not change the setting");
        });
    }
}
