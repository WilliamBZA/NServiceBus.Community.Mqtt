#nullable enable

namespace NServiceBus.Community.Mqtt.TransportTests;

using System.Text;
using NServiceBus.Community.Mqtt.TestBroker;
using NServiceBus.Logging;
using NServiceBus.Transport;
using NServiceBus.Transport.Mqtt;

/// <summary>
/// What the standard transport suite does not cover about receiving: the MQTT mapping of a queue, runtime concurrency changes, poison
/// payloads, reconnect, takeover by a second consumer, redelivery and purge on startup. Each test uses a queue of its own.
/// </summary>
public class ReceivingTests : NServiceBus.TransportTests.NServiceBusTransportTest
{
    [Test]
    public async Task Should_fail_startup_naming_host_and_port_when_the_broker_is_unreachable()
    {
        var port = Wait.FreePort();
        using var pump = new MqttMessagePump(
            "main",
            "Unreachable.Queue",
            new MqttConnectionSettings("127.0.0.1", port, TimeSpan.FromMinutes(5)),
            TransportTransactionMode.ReceiveOnly,
            usePublishSubscribe: true,
            explicitTopics: [],
            onCritical: (_, _, _) => { });
        await pump.Initialize(new PushRuntimeSettings(1), (_, _) => Task.CompletedTask, (_, _) => Task.FromResult(ErrorHandleResult.Handled));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => pump.StartReceive());

        Assert.That(exception!.Message, Does.Contain($"127.0.0.1:{port}"));
    }

    [Test]
    public async Task Should_never_run_more_handlers_than_the_concurrency_limit()
    {
        var queue = MqttTestEndpoint.NewQueue("Limit");
        var handlers = new BlockingHandlers();
        await using var endpoint = new MqttTestEndpoint(queue, new() { Concurrency = 4 });

        try
        {
            await endpoint.Initialize(handlers.Handle);
            await endpoint.Start();

            for (var i = 0; i < 20; i++)
            {
                await endpoint.Send(queue);
            }

            await Wait.Until(() => handlers.Running == 4, "four handlers to be running");

            // the other sixteen would have started by now if the limit were not honoured
            await Task.Delay(1000);
            Assert.That(handlers.MaxRunning, Is.EqualTo(4));

            handlers.Release();
            await Wait.Until(() => handlers.Completed == 20, "all 20 messages to be handled");
            Assert.That(handlers.MaxRunning, Is.EqualTo(4), "the limit was never exceeded");
        }
        finally
        {
            handlers.Release();
            await BrokerStateCleaner.Clear([endpoint.Topic, MqttAddress.ToTopic(endpoint.ErrorQueue)]);
        }
    }

    [Test]
    public async Task Should_apply_a_raised_concurrency_limit_without_a_restart()
    {
        var queue = MqttTestEndpoint.NewQueue("Raise");
        var handlers = new BlockingHandlers();
        await using var endpoint = new MqttTestEndpoint(queue, new() { Concurrency = 2 });

        try
        {
            await endpoint.Initialize(handlers.Handle);
            await endpoint.Start();

            for (var i = 0; i < 20; i++)
            {
                await endpoint.Send(queue);
            }

            await Wait.Until(() => handlers.Running == 2, "two handlers to be running");
            await Task.Delay(500);
            Assert.That(handlers.MaxRunning, Is.EqualTo(2), "only two handlers run before the limit is raised");

            await endpoint.Receiver.ChangeConcurrency(new PushRuntimeSettings(8));

            await Wait.Until(() => handlers.Running == 8, "eight handlers to be running");
            await Task.Delay(500);
            Assert.That(handlers.MaxRunning, Is.EqualTo(8), "and no more than the new limit run after it");

            handlers.Release();
            await Wait.Until(() => handlers.Completed == 20, "all 20 messages to be handled");
        }
        finally
        {
            handlers.Release();
            await BrokerStateCleaner.Clear([endpoint.Topic, MqttAddress.ToTopic(endpoint.ErrorQueue)]);
        }
    }

    [Test]
    public async Task Should_discard_a_poison_payload_with_one_error_log_and_handle_the_next_message()
    {
        var queue = MqttTestEndpoint.NewQueue("Poison");
        var handled = 0;
        await using var endpoint = new MqttTestEndpoint(queue);

        try
        {
            await endpoint.Initialize((_, _) =>
            {
                Interlocked.Increment(ref handled);
                return Task.CompletedTask;
            });
            await endpoint.Start();
            LogFactory.LogItems.Clear();

            await RawMqtt.Publish(endpoint.Topic, Encoding.UTF8.GetBytes("this is not a message"));
            await endpoint.Send(queue);

            await Wait.Until(() => Volatile.Read(ref handled) == 1, "the valid message to be handled");
            await Task.Delay(500);

            Assert.That(handled, Is.EqualTo(1), "exactly one message is handled");
            var errors = LogFactory.LogItems.Where(item => item.Level >= LogLevel.Error).Select(item => item.Message).ToArray();
            Assert.That(errors, Has.Length.EqualTo(1), string.Join(Environment.NewLine, errors));
            Assert.That(errors[0], Does.Contain(endpoint.Topic));
        }
        finally
        {
            await BrokerStateCleaner.Clear([endpoint.Topic, MqttAddress.ToTopic(endpoint.ErrorQueue)]);
        }
    }

    [Test]
    public async Task Should_resume_receiving_after_the_connection_to_the_broker_is_cut_and_restored()
    {
        var queue = MqttTestEndpoint.NewQueue("Reconnect");
        var received = new List<string>();
        using var proxy = new TcpProxy(MqttTestBroker.Host, MqttTestBroker.Port);
        await using var endpoint = new MqttTestEndpoint(queue, new() { Host = proxy.Host, Port = proxy.Port });
        await using var sender = new MqttTestEndpoint(MqttTestEndpoint.NewQueue("Sender"));

        try
        {
            await sender.Initialize((_, _) => Task.CompletedTask);
            await endpoint.Initialize((context, _) =>
            {
                lock (received)
                {
                    received.Add(context.Headers["Order"]);
                }

                return Task.CompletedTask;
            });
            await endpoint.Start();

            await sender.Send(queue, new() { ["Order"] = "before" });
            await Wait.Until(() => Count(received) == 1, "the first message");

            proxy.Cut();
            await Task.Delay(2000);
            await sender.Send(queue, new() { ["Order"] = "while cut" });
            proxy.Restore();

            await Wait.Until(() => Count(received) == 2, "the message sent while the connection was cut");

            await sender.Send(queue, new() { ["Order"] = "after" });
            await Wait.Until(() => Count(received) == 3, "a message sent after the connection came back");

            Assert.That(received, Is.EqualTo(new[] { "before", "while cut", "after" }));
        }
        finally
        {
            await BrokerStateCleaner.Clear([endpoint.Topic, MqttAddress.ToTopic(endpoint.ErrorQueue), sender.Topic, MqttAddress.ToTopic(sender.ErrorQueue)]);
        }

        static int Count(List<string> list)
        {
            lock (list)
            {
                return list.Count;
            }
        }
    }

    [Test]
    public async Task Should_report_a_critical_error_and_stay_disconnected_when_a_second_consumer_takes_the_queue()
    {
        var queue = MqttTestEndpoint.NewQueue("Takeover");
        var firstCritical = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCritical = false;
        var handledByFirst = 0;
        var handledBySecond = 0;
        await using var first = new MqttTestEndpoint(queue, new() { OnCriticalError = (message, _, _) => firstCritical.TrySetResult(message) });
        await using var second = new MqttTestEndpoint(queue, new() { OnCriticalError = (_, _, _) => secondCritical = true });

        try
        {
            await first.Initialize((_, _) =>
            {
                Interlocked.Increment(ref handledByFirst);
                return Task.CompletedTask;
            });
            await first.Start();

            await second.Initialize((_, _) =>
            {
                Interlocked.Increment(ref handledBySecond);
                return Task.CompletedTask;
            });
            await second.Start();

            var message = await Wait.For(firstCritical.Task, "the first consumer to be told it was displaced");
            Assert.Multiple(() =>
            {
                Assert.That(message, Does.Contain(first.Topic), "names the queue");
                Assert.That(message, Does.Contain("Only one consumer per queue is supported"));
                Assert.That(message, Does.Contain("own endpoint name"));
            });

            // had the first consumer reconnected, it would have taken the session back from the second one
            await Task.Delay(TimeSpan.FromSeconds(10));
            await second.Send(queue);

            await Wait.Until(() => Volatile.Read(ref handledBySecond) == 1, "the second consumer to receive the message");
            await Task.Delay(500);
            Assert.Multiple(() =>
            {
                Assert.That(handledBySecond, Is.EqualTo(1));
                Assert.That(handledByFirst, Is.Zero, "the displaced consumer receives nothing");
                Assert.That(secondCritical, Is.False, "the second consumer is not displaced in turn");
            });
        }
        finally
        {
            await BrokerStateCleaner.Clear([first.Topic, MqttAddress.ToTopic(first.ErrorQueue)]);
        }
    }

    [Test]
    public async Task Should_wait_for_a_running_handler_when_stopping_and_deliver_nothing_after_it()
    {
        var queue = MqttTestEndpoint.NewQueue("Stop");
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = 0;
        var handlerWasCancelled = true;
        await using var endpoint = new MqttTestEndpoint(queue);

        try
        {
            await endpoint.Initialize(async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref handled);
                started.TrySetResult(true);
                await release.Task;
                handlerWasCancelled = cancellationToken.IsCancellationRequested;
            });
            await endpoint.Start();
            await endpoint.Send(queue);
            await Wait.For(started.Task, "the handler to start");

            var stopping = endpoint.Stop();
            await Task.Delay(1000);
            Assert.That(stopping.IsCompleted, Is.False, "stop waits for the handler that is running");

            release.SetResult(true);
            await stopping.WaitAsync(Wait.DefaultTimeout);
            Assert.That(handlerWasCancelled, Is.False, "a stop that is not cancelled does not cancel the handler");

            // the pump holds no connection now, so this stays in the queue
            await endpoint.Send(queue);
            await Task.Delay(1000);
            Assert.That(handled, Is.EqualTo(1), "nothing is handled once stop has completed");
        }
        finally
        {
            release.TrySetResult(true);
            await BrokerStateCleaner.Clear([endpoint.Topic, MqttAddress.ToTopic(endpoint.ErrorQueue)]);
        }
    }

    [Test]
    public async Task Should_redeliver_a_message_whose_handler_was_cancelled_by_a_cancelled_stop()
    {
        var queue = MqttTestEndpoint.NewQueue("Redeliver");
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recoverabilityInvoked = false;
        var redelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = new MqttTestEndpoint(queue);
        await using var second = new MqttTestEndpoint(queue);

        try
        {
            await first.Initialize(
                async (_, cancellationToken) =>
                {
                    started.TrySetResult(true);
                    await Task.Delay(Wait.DefaultTimeout, cancellationToken);
                },
                (_, _) =>
                {
                    recoverabilityInvoked = true;
                    return Task.FromResult(ErrorHandleResult.Handled);
                });
            await first.Start();
            await first.Send(queue);
            await Wait.For(started.Task, "the handler to start");

            await first.Stop(new CancellationToken(true));
            Assert.That(recoverabilityInvoked, Is.False, "a handler cancelled by the stop is not a failure");

            await second.Initialize((_, _) =>
            {
                redelivered.TrySetResult(true);
                return Task.CompletedTask;
            });
            await second.Start();

            await Wait.For(redelivered.Task, "the unacknowledged message to be delivered again");
        }
        finally
        {
            await BrokerStateCleaner.Clear([first.Topic, MqttAddress.ToTopic(first.ErrorQueue)]);
        }
    }

    [TestCase(true, 0)]
    [TestCase(false, 3)]
    public async Task Should_drop_the_backlog_only_when_purging_on_startup(bool purgeOnStartup, int expectedBacklog)
    {
        var queue = MqttTestEndpoint.NewQueue("Purge");
        var received = new List<string>();
        await using var sender = new MqttTestEndpoint(MqttTestEndpoint.NewQueue("Sender"));

        try
        {
            await sender.Initialize((_, _) => Task.CompletedTask);

            // the endpoint ran before, so the broker holds the session of its queue while it is offline
            await using (var earlier = new MqttTestEndpoint(queue))
            {
                await earlier.Initialize((_, _) => Task.CompletedTask);
                await earlier.Start();
                await earlier.Stop();
            }

            for (var i = 0; i < 3; i++)
            {
                await sender.Send(queue, new() { ["Order"] = "backlog" });
            }

            await using var endpoint = new MqttTestEndpoint(queue, new() { PurgeOnStartup = purgeOnStartup });
            await endpoint.Initialize((context, _) =>
            {
                lock (received)
                {
                    received.Add(context.Headers["Order"]);
                }

                return Task.CompletedTask;
            });
            await endpoint.Start();

            await sender.Send(queue, new() { ["Order"] = "marker" });

            await Wait.Until(() => Snapshot().Contains("marker"), "the message sent after startup");
            await Task.Delay(500);

            Assert.That(Snapshot().Count(order => order == "backlog"), Is.EqualTo(expectedBacklog));
        }
        finally
        {
            await BrokerStateCleaner.Clear([MqttAddress.ToTopic(queue), MqttAddress.ToTopic($"{queue}.error"), sender.Topic, MqttAddress.ToTopic(sender.ErrorQueue)]);
        }

        string[] Snapshot()
        {
            lock (received)
            {
                return received.ToArray();
            }
        }
    }

    [Test]
    public async Task Should_purge_before_anything_the_startup_tasks_send_and_keep_what_they_send()
    {
        // NServiceBus starts the transport, then runs the feature startup tasks, which can send to the endpoint's own queue, and only then receives
        await using var endpoints = new EndpointSet();
        var sender = endpoints.Create("Sender");
        var earlier = endpoints.Create("Purge");
        await sender.Initialize((_, _) => Task.CompletedTask);
        await earlier.Initialize((_, _) => Task.CompletedTask);
        await earlier.Start();
        await earlier.Stop();
        for (var i = 0; i < 3; i++)
        {
            await sender.Send(earlier.Queue, new() { ["Order"] = "backlog" });
        }

        var endpoint = endpoints.CreateOn(earlier.Queue, new() { PurgeOnStartup = true });
        var inbox = new Inbox();
        await endpoint.Initialize(inbox.Handle);
        await sender.Send(endpoint.Queue, new() { ["Order"] = "sent by a startup task" });
        await endpoint.Start();

        await inbox.WaitFor(1, "the message sent after the transport was initialized");
        await inbox.AssertCount(1, "and nothing from the backlog");
        Assert.That(inbox.Items.Single().Headers["Order"], Is.EqualTo("sent by a startup task"));
    }

    [Test]
    public async Task Should_purge_even_when_the_host_does_not_set_up_infrastructure()
    {
        await using var endpoints = new EndpointSet();
        var sender = endpoints.Create("Sender");
        var earlier = endpoints.Create("Purge");
        await sender.Initialize((_, _) => Task.CompletedTask);
        await earlier.Initialize((_, _) => Task.CompletedTask);
        await earlier.Start();
        await earlier.Stop();
        for (var i = 0; i < 3; i++)
        {
            await sender.Send(earlier.Queue, new() { ["Order"] = "backlog" });
        }

        var endpoint = endpoints.CreateOn(earlier.Queue, new() { PurgeOnStartup = true, SetupInfrastructure = false });
        var inbox = new Inbox();
        await endpoint.Initialize(inbox.Handle);
        await endpoint.Start();
        await sender.Send(endpoint.Queue, new() { ["Order"] = "marker" });

        await inbox.WaitFor(1, "the message sent after startup");
        await inbox.AssertCount(1, "and nothing from the backlog");
        Assert.That(inbox.Items.Single().Headers["Order"], Is.EqualTo("marker"));
    }

    sealed class BlockingHandlers
    {
        public int Running => Volatile.Read(ref running);

        public int MaxRunning => Volatile.Read(ref maxRunning);

        public int Completed => Volatile.Read(ref completed);

        public async Task Handle(MessageContext context, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref running);
            int seen;
            while ((seen = Volatile.Read(ref maxRunning)) < now && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen)
            {
            }

            try
            {
                await gate.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref running);
                Interlocked.Increment(ref completed);
            }
        }

        public void Release() => gate.TrySetResult(true);

        readonly TaskCompletionSource<bool> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int running;
        int maxRunning;
        int completed;
    }
}
