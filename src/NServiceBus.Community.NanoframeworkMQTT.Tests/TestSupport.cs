using System;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Threading;
using Contracts;
using Microsoft.Extensions.Logging;
using nanoFramework.TestFramework;
using NServiceBus.Community.NanoframeworkMQTT.Infrastructure;

namespace NServiceBus.Community.NanoframeworkMQTT.Tests
{
    internal delegate bool Condition();

    internal delegate void Check();

    internal delegate void HandlerAction(object message, IMessageHandlerContext context);

    /// <summary>A handler that runs the action a test gives it, and keeps what it was given.</summary>
    internal sealed class ActionHandler : IHandleMessages
    {
        readonly HandlerAction action;
        readonly ArrayList messages = new ArrayList();
        readonly ArrayList headers = new ArrayList();
        int invocations;

        public ActionHandler(HandlerAction action)
        {
            this.action = action;
        }

        public int Invocations
        {
            get
            {
                lock (messages)
                {
                    return invocations;
                }
            }
        }

        public ArrayList Messages
        {
            get
            {
                lock (messages)
                {
                    return (ArrayList)messages.Clone();
                }
            }
        }

        /// <summary>The headers each attempt saw, as a copy taken on entry.</summary>
        public ArrayList HeadersSeen
        {
            get
            {
                lock (messages)
                {
                    return (ArrayList)headers.Clone();
                }
            }
        }

        public void Handle(object message, IMessageHandlerContext context)
        {
            lock (messages)
            {
                invocations++;
                messages.Add(message);

                var copy = new Hashtable();
                foreach (DictionaryEntry entry in context.MessageHeaders)
                {
                    copy[entry.Key] = entry.Value;
                }

                headers.Add(copy);
            }

            if (action != null)
            {
                action(message, context);
            }
        }
    }

    internal sealed class LogEntry
    {
        public LogEntry(LogLevel level, string message, Exception exception)
        {
            Level = level;
            Message = message;
            Exception = exception;
        }

        public LogLevel Level { get; private set; }

        public string Message { get; private set; }

        public Exception Exception { get; private set; }
    }

    internal sealed class RecordingLogger : ILogger
    {
        readonly ArrayList entries = new ArrayList();

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log(LogLevel logLevel, EventId eventId, string state, Exception exception, MethodInfo format)
        {
            lock (entries)
            {
                entries.Add(new LogEntry(logLevel, state, exception));
            }
        }

        public ArrayList Entries
        {
            get
            {
                lock (entries)
                {
                    return (ArrayList)entries.Clone();
                }
            }
        }

        /// <summary>The entries of a level whose message contains the text.</summary>
        public ArrayList Find(LogLevel level, string text)
        {
            var found = new ArrayList();
            var all = Entries;
            for (var i = 0; i < all.Count; i++)
            {
                var entry = (LogEntry)all[i];
                if (entry.Level == level && entry.Message.IndexOf(text) >= 0)
                {
                    found.Add(entry);
                }
            }

            return found;
        }

        public bool Has(LogLevel level, string text)
        {
            return Find(level, text).Count > 0;
        }
    }

    internal sealed class FixedClock : IClock
    {
        public FixedClock(DateTime now)
        {
            Now = now;
        }

        public DateTime Now;

        /// <summary>The next read of the clock throws, once.</summary>
        public bool FailNext;

        public DateTime UtcNow
        {
            get
            {
                if (FailNext)
                {
                    FailNext = false;
                    throw new InvalidOperationException("The clock failed.");
                }

                return Now;
            }
        }
    }

    /// <summary>Hands out <c>id-1</c>, <c>id-2</c> and so on.</summary>
    internal sealed class SequenceIds : IIdGenerator
    {
        int next = 1;

        public string NewId()
        {
            lock (this)
            {
                return "id-" + next++;
            }
        }
    }

    /// <summary>A delay that does not wait. It records what it was asked for, and answers as the test says.</summary>
    internal sealed class RecordingDelay : IDelay
    {
        readonly ArrayList requested = new ArrayList();

        /// <summary>When set, the delay waits for the interrupt, as a real one does, instead of returning at once.</summary>
        public bool WaitForInterrupt;

        public ArrayList Requested
        {
            get
            {
                lock (requested)
                {
                    return (ArrayList)requested.Clone();
                }
            }
        }

        /// <summary>The requested delays in seconds.</summary>
        public int[] Seconds
        {
            get
            {
                var all = Requested;
                var result = new int[all.Count];
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = (int)(((TimeSpan)all[i]).Ticks / TimeSpan.TicksPerSecond);
                }

                return result;
            }
        }

        public bool Wait(TimeSpan duration, ManualResetEvent interrupt)
        {
            lock (requested)
            {
                requested.Add(duration);
            }

            if (WaitForInterrupt)
            {
                return interrupt.WaitOne();
            }

            // a moment, so that the thread that waits does not spin while the test looks at the state
            Thread.Sleep(5);
            return interrupt.WaitOne(0, false);
        }
    }

    /// <summary>What the broker was given, decoded.</summary>
    internal static class Wire
    {
        public static WireEnvelope At(EndpointHarness harness, string topic, int index)
        {
            var published = harness.Broker.PublishedTo(topic);
            Assert.IsTrue(index < published.Count, "expected a message on '" + topic + "' at position " + index + " but there are " + published.Count);
            return WireFormat.Decode(((PublishedMessage)published[index]).Payload);
        }

        public static WireEnvelope First(EndpointHarness harness, string topic)
        {
            return At(harness, topic, 0);
        }

        public static string Header(WireEnvelope envelope, string name)
        {
            return envelope.Headers[name] as string;
        }

        public static string Text(byte[] body)
        {
            return Encoding.UTF8.GetString(body, 0, body.Length);
        }
    }

    internal static class Samples
    {
        /// <summary>How a .NET endpoint lists a command type: with its assembly.</summary>
        public const string OpenValveTypes = "Contracts.OpenValve, Interop, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        public const string AlwaysFailsTypes = "Contracts.AlwaysFails, Interop, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        /// <summary>An <c>OpenValve</c> from a .NET endpoint to the device's queue.</summary>
        public static void DeliverOpenValve(EndpointHarness harness, string id)
        {
            harness.Deliver(id, EndpointHarness.DotNetHeaders(id, OpenValveTypes, "Send"), EndpointHarness.Utf8("{\"ValveId\":\"valve-" + id + "\",\"Percent\":50}"));
        }

        public static void DeliverAlwaysFails(EndpointHarness harness, string id)
        {
            harness.Deliver(id, EndpointHarness.DotNetHeaders(id, AlwaysFailsTypes, "Send"), EndpointHarness.Utf8("{\"Reason\":\"because\"}"));
        }

        public const string ValveOpenedTypes =
            "Contracts.ValveOpened, Interop, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.ValveEvent, Interop, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;"
            + "Contracts.IAlarm, Interop, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAudited, Interop, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        public const string PriceChangedTypes = "Contracts.PriceChanged, Interop, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        public const string ValveOpenedBody = "{\"Percent\":100,\"ValveId\":\"V-1\"}";

        /// <summary>
        /// A <c>ValveOpened</c> published by a .NET endpoint: one copy on the topic of every type in its hierarchy, with the same payload,
        /// which reaches the devices that are subscribed to those topics.
        /// </summary>
        public static void PublishValveOpened(FakeBroker broker, string id)
        {
            var headers = EndpointHarness.DotNetHeaders(id, ValveOpenedTypes, "Publish");
            var payload = WireFormat.Encode(id, headers, EndpointHarness.Utf8(ValveOpenedBody));

            var hierarchy = EventTypeHierarchy.Enumerate(typeof(ValveOpened));
            for (var i = 0; i < hierarchy.Length; i++)
            {
                broker.PublishAsForeignClient(EventTopic.ToTopic(hierarchy[i]), payload);
            }
        }

        public static void PublishPriceChanged(FakeBroker broker, string id)
        {
            var headers = EndpointHarness.DotNetHeaders(id, PriceChangedTypes, "Publish");
            var payload = WireFormat.Encode(id, headers, EndpointHarness.Utf8("{\"Sku\":\"sku-1\",\"Price\":9.5}"));
            broker.PublishAsForeignClient(EventTopic.ToTopic(typeof(PriceChanged)), payload);
        }
    }

    /// <summary>An endpoint under test with its fake broker, clock, ids and delay, and a record of the critical errors.</summary>
    internal sealed class EndpointHarness
    {
        public static readonly DateTime Time = new DateTime(639266193301234567L, DateTimeKind.Utc);

        public readonly FakeBroker Broker;
        public readonly FakeMqttConnection Connection;
        public readonly RecordingLogger Log = new RecordingLogger();
        public readonly RecordingDelay Delay = new RecordingDelay();
        public readonly SequenceIds Ids = new SequenceIds();
        public readonly FixedClock Clock = new FixedClock(Time);
        public readonly DeviceEndpointConfiguration Configuration;
        public readonly ArrayList CriticalErrors = new ArrayList();

        public DeviceEndpoint Endpoint;

        public EndpointHarness(string endpointName)
            : this(endpointName, new FakeBroker())
        {
        }

        /// <summary>A device on a broker that other devices share.</summary>
        public EndpointHarness(string endpointName, FakeBroker broker)
        {
            Broker = broker;
            Connection = new FakeMqttConnection(Broker);
            Configuration = new DeviceEndpointConfiguration(endpointName, "broker.test", 1883);
            Configuration.Logger = Log;
            Configuration.StopDrainTimeout = TimeSpan.FromSeconds(5);
            Configuration.OnCriticalError = OnCriticalError;
        }

        public EndpointHarness()
            : this("Device_01")
        {
        }

        public string ClientId
        {
            get { return MqttClientId.ForQueue(MqttAddress.ToTopic(EndpointName)); }
        }

        public string EndpointName
        {
            get { return Configuration.EndpointName; }
        }

        public string QueueTopic
        {
            get { return MqttAddress.ToTopic(EndpointName); }
        }

        public EndpointServices Services
        {
            get { return new EndpointServices(Clock, Delay, Ids); }
        }

        public DeviceEndpoint Start()
        {
            Endpoint = DeviceEndpoint.Start(Configuration, Connection, Services);
            return Endpoint;
        }

        public void Stop()
        {
            if (Endpoint != null)
            {
                Endpoint.Stop();
            }
        }

        void OnCriticalError(string description, Exception exception)
        {
            lock (CriticalErrors)
            {
                CriticalErrors.Add(new LogEntry(LogLevel.Critical, description, exception));
            }
        }

        public ArrayList Criticals
        {
            get
            {
                lock (CriticalErrors)
                {
                    return (ArrayList)CriticalErrors.Clone();
                }
            }
        }

        /// <summary>Publishes a message to the device's queue as a .NET endpoint would.</summary>
        public void Deliver(string id, Hashtable headers, byte[] body)
        {
            Broker.PublishAsForeignClient(QueueTopic, WireFormat.Encode(id, headers, body));
        }

        /// <summary>The headers of a message from a .NET endpoint: the type names, the intent and the reply-to address.</summary>
        public static Hashtable DotNetHeaders(string id, string enclosedMessageTypes, string intent)
        {
            var headers = new Hashtable();
            headers[HeaderNames.MessageId] = id;
            headers[HeaderNames.MessageIntent] = intent;
            headers[HeaderNames.EnclosedMessageTypes] = enclosedMessageTypes;
            headers[HeaderNames.ContentType] = HeaderNames.JsonContentType;
            headers[HeaderNames.ConversationId] = "conversation-of-" + id;
            headers[HeaderNames.CorrelationId] = "correlation-of-" + id;
            headers[HeaderNames.ReplyToAddress] = "Plant";
            headers[HeaderNames.OriginatingEndpoint] = "Plant";
            return headers;
        }

        public static byte[] Utf8(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        public static void WaitFor(Condition condition, string what)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow.Ticks > deadline.Ticks)
                {
                    Assert.IsTrue(false, "Timed out waiting for " + what);
                }

                Thread.Sleep(10);
            }
        }

        /// <summary>Gives the endpoint's threads time to do something that must not happen, and then checks that it did not.</summary>
        public static void Settle()
        {
            Thread.Sleep(150);
        }
    }
}
