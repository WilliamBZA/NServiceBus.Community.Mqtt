using Contracts;
using NServiceBus;

namespace InteropHost;

static class InteropEndpoints
{
    /// <summary>The endpoint name the sample device app runs as (<c>SampleSettings.EndpointName</c>).</summary>
    public const string Device = "NanoInterop_Device";

    public const string Host = "NanoInterop_Host";

    public const string ErrorQueue = "error";

    /// <summary>
    /// The immediate retries of the sample device app, which uses the default: one attempt plus five retries. The failing handler of the sample
    /// counts its attempts in the exception message, so the host can see that the device retried as many times as it was configured to.
    /// </summary>
    public const int ExpectedAttempts = 6;
}

enum Outcome
{
    Passed,
    Failed,
    Skipped
}

record CheckResult(Outcome Outcome, string Name, string Detail)
{
    public static CheckResult Passed(string name, string detail = "") => new(Outcome.Passed, name, detail);

    public static CheckResult Failed(string name, string detail) => new(Outcome.Failed, name, detail);

    public static CheckResult Skipped(string name, string detail) => new(Outcome.Skipped, name, detail);

    public override string ToString()
    {
        var label = Outcome switch
        {
            Outcome.Passed => "PASS",
            Outcome.Failed => "FAIL",
            _ => "SKIP"
        };

        return Detail.Length == 0 ? $"[{label}] {Name}" : $"[{label}] {Name} - {Detail}";
    }
}

/// <summary>What a handler of the interop host saw: the message's ID and headers, and the part of the message the checks look at.</summary>
record Observation(string ValveId, IReadOnlyDictionary<string, string> Headers, int Percent = 0, bool IsOpen = false)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>Where the handlers put what they saw, for the checks to look at. The checks run one after another, so nothing here is reset.</summary>
sealed class Mailbox
{
    public void Add(Observation observation)
    {
        lock (observations)
        {
            observations.Add(observation);
        }
    }

    public List<Observation> Matching(Func<Observation, bool> predicate)
    {
        lock (observations)
        {
            return observations.Where(predicate).ToList();
        }
    }

    public async Task<Observation?> WaitFor(Func<Observation, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var found = Matching(predicate).FirstOrDefault();
            if (found is not null)
            {
                return found;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    readonly List<Observation> observations = [];
}

static class Mailboxes
{
    public static readonly Mailbox Responses = new();
    public static readonly Mailbox OpenedEvents = new();
    public static readonly Mailbox Acknowledgements = new();
}

class ValveStatusResponseHandler : IHandleMessages<ValveStatusResponse>
{
    public Task Handle(ValveStatusResponse message, IMessageHandlerContext context)
    {
        Mailboxes.Responses.Add(new Observation(message.ValveId, new Dictionary<string, string>(context.MessageHeaders), message.Percent, message.IsOpen));
        return Task.CompletedTask;
    }
}

class ValveOpenedHandler : IHandleMessages<ValveOpened>
{
    public Task Handle(ValveOpened message, IMessageHandlerContext context)
    {
        Mailboxes.OpenedEvents.Add(new Observation(message.ValveId, new Dictionary<string, string>(context.MessageHeaders), message.Percent));
        return Task.CompletedTask;
    }
}

class ValveEventAcknowledgedHandler : IHandleMessages<ValveEventAcknowledged>
{
    public Task Handle(ValveEventAcknowledged message, IMessageHandlerContext context)
    {
        Mailboxes.Acknowledgements.Add(new Observation(message.ValveId, new Dictionary<string, string>(context.MessageHeaders)));
        return Task.CompletedTask;
    }
}

/// <summary>The checks of the <c>device-interop-verification</c> capability. Each one has its own timeout and reports instead of throwing.</summary>
sealed class InteropChecks(IMessageSession endpoint, string server, int port, TimeSpan timeout)
{
    public async Task<CheckResult> CommandAndReply()
    {
        const string name = "A .NET command is handled by the device, which replies, and the reply is correlated to the command";
        try
        {
            var messageId = Guid.NewGuid().ToString();
            var valveId = "interop-" + messageId[..8];
            currentValveId = valveId;

            var options = new SendOptions();
            options.SetMessageId(messageId);
            await endpoint.Send(new OpenValve { ValveId = valveId, Percent = 60 }, options).ConfigureAwait(false);

            var response = await Mailboxes.Responses.WaitFor(observation => observation.Header(Headers.RelatedTo) == messageId, timeout).ConfigureAwait(false);
            if (response is null)
            {
                return CheckResult.Failed(name, $"the device did not reply within {timeout.TotalSeconds:0} s. Is the sample running, connected to the same broker, as '{InteropEndpoints.Device}'?");
            }

            if (response.ValveId != valveId || !response.IsOpen || response.Percent != 60)
            {
                return CheckResult.Failed(name, $"the reply has valve '{response.ValveId}', open={response.IsOpen}, {response.Percent} %, instead of '{valveId}', open, 60 %");
            }

            var intent = response.Header(Headers.MessageIntent);
            return intent == nameof(MessageIntent.Reply)
                ? CheckResult.Passed(name)
                : CheckResult.Failed(name, $"the reply has the intent '{intent}' instead of 'Reply'");
        }
        catch (Exception exception)
        {
            return CheckResult.Failed(name, exception.Message);
        }
    }

    public async Task<CheckResult> DeviceEventReachesDotNetSubscriber()
    {
        const string name = "A device event reaches a .NET subscriber";
        try
        {
            if (currentValveId is null)
            {
                return CheckResult.Failed(name, "the first check did not send a command, so the device had no reason to publish");
            }

            var valveId = currentValveId;
            var opened = await Mailboxes.OpenedEvents.WaitFor(observation => observation.ValveId == valveId, timeout).ConfigureAwait(false);
            if (opened is null)
            {
                return CheckResult.Failed(name, $"no ValveOpened event for '{valveId}' arrived within {timeout.TotalSeconds:0} s");
            }

            // the device publishes one copy per type in the hierarchy, and the .NET endpoint must handle the event once
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            var count = Mailboxes.OpenedEvents.Matching(observation => observation.ValveId == valveId).Count;
            if (count != 1)
            {
                return CheckResult.Failed(name, $"the event was handled {count} times instead of once");
            }

            var origin = opened.Header(Headers.OriginatingEndpoint);
            return origin == InteropEndpoints.Device
                ? CheckResult.Passed(name)
                : CheckResult.Failed(name, $"the event says it came from '{origin}' instead of '{InteropEndpoints.Device}'");
        }
        catch (Exception exception)
        {
            return CheckResult.Failed(name, exception.Message);
        }
    }

    public async Task<CheckResult> DotNetEventReachesDeviceThroughBaseType()
    {
        const string name = "A .NET event reaches the device through its subscription to the base type";
        try
        {
            var valveId = "from-host-" + Guid.NewGuid().ToString()[..8];
            await endpoint.Publish(new ValveOpened { ValveId = valveId, Percent = 100 }).ConfigureAwait(false);

            // the device only handles ValveEvent, the base type, and answers with a command
            var acknowledgement = await Mailboxes.Acknowledgements.WaitFor(observation => observation.ValveId == valveId, timeout).ConfigureAwait(false);
            return acknowledgement is not null
                ? CheckResult.Passed(name)
                : CheckResult.Failed(name, $"the device did not acknowledge the event within {timeout.TotalSeconds:0} s");
        }
        catch (Exception exception)
        {
            return CheckResult.Failed(name, exception.Message);
        }
    }

    public async Task<CheckResult> FailedMessageEndsUpInTheErrorQueue()
    {
        const string name = "A message the device always fails ends up in the error queue, with failure headers, after the configured retries";
        try
        {
            var messageId = Guid.NewGuid().ToString();
            var options = new SendOptions();
            options.SetMessageId(messageId);
            await endpoint.Send(new AlwaysFails { Reason = "interop check" }, options).ConfigureAwait(false);

            var failed = await ErrorQueueReader.WaitFor(server, port, InteropEndpoints.ErrorQueue, messageId, timeout).ConfigureAwait(false);
            if (failed is null)
            {
                return CheckResult.Failed(name, $"the message did not arrive in the error queue '{InteropEndpoints.ErrorQueue}' within {timeout.TotalSeconds:0} s. A .NET endpoint with installers has to declare the queue, and this host does");
            }

            var problems = new List<string>();
            // the .NET transport writes its own queue as the topic (NanoInterop/Device); both spellings are the same address
            if (!failed.TryGetValue("NServiceBus.FailedQ", out var failedQueue) || failedQueue.Replace('_', '/') != InteropEndpoints.Device.Replace('_', '/'))
            {
                problems.Add($"NServiceBus.FailedQ is '{failedQueue}' instead of '{InteropEndpoints.Device}'");
            }

            Expect(failed, "NServiceBus.ProcessingEndpoint", InteropEndpoints.Device, problems);
            Expect(failed, "NServiceBus.ExceptionInfo.ExceptionType", "System.InvalidOperationException", problems);

            if (!failed.TryGetValue("NServiceBus.TimeOfFailure", out var timeOfFailure) || !TryParseTimeOfFailure(timeOfFailure))
            {
                problems.Add($"NServiceBus.TimeOfFailure is '{timeOfFailure}'");
            }

            if (!failed.TryGetValue("NServiceBus.ExceptionInfo.Message", out var message) || !message.Contains($"attempt {InteropEndpoints.ExpectedAttempts}", StringComparison.Ordinal))
            {
                problems.Add($"the exception message is '{message}' and does not say it was the {InteropEndpoints.ExpectedAttempts}th attempt (one attempt and five immediate retries)");
            }

            if (!failed.ContainsKey("NServiceBus.ExceptionInfo.StackTrace"))
            {
                problems.Add("NServiceBus.ExceptionInfo.StackTrace is missing");
            }

            return problems.Count == 0 ? CheckResult.Passed(name) : CheckResult.Failed(name, string.Join("; ", problems));
        }
        catch (Exception exception)
        {
            return CheckResult.Failed(name, exception.Message);
        }
    }

    static void Expect(IReadOnlyDictionary<string, string> headers, string header, string expected, List<string> problems)
    {
        if (!headers.TryGetValue(header, out var actual) || actual != expected)
        {
            problems.Add($"{header} is '{actual}' instead of '{expected}'");
        }
    }

    static bool TryParseTimeOfFailure(string text)
    {
        try
        {
            DateTimeOffsetHelper.ToDateTimeOffset(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    string? currentValveId;
}
