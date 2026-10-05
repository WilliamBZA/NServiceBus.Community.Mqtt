# Known exclusions

The standard NServiceBus acceptance suite runs unmodified against this transport, with the exceptions below. Each one is a test that
cannot pass because of a limitation of the transport, and each was approved before it was excluded.

An excluded test is filtered out by `src/NServiceBus.Community.Mqtt.AcceptanceTests/acceptance.runsettings`, which `dotnet test` and CI both
use. The suite sources are never edited. A unit test (`KnownExclusionsTests`) fails if this file and the filter list different tests.

A test is only added here with the user's explicit approval, except tests whose purpose is several instances sharing one input queue:
the decision below pre-approves those.

| Test | Limitation | Approved by | Date |
|---|---|---|---|
| `When_publishing_to_scaled_out_subscribers` | No scale-out. The test runs two instances of one endpoint on the same input queue and expects each event to be handled exactly once per endpoint. That needs competing consumers on one queue. This transport has one consumer per queue, because it targets IoT devices where every device is its own endpoint, so a second consumer takes the queue over from the first. | The user (decision to not support scale-out) | 2026-10-02 |
