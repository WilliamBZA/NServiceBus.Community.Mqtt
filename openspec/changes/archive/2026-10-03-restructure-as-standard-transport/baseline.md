# Baseline (task 5.5)

Both broker-backed suites were run once against the **unchanged transport** (the pre-rework pump, dispatcher and subscription manager, only moved and renamed), on Mosquitto 2.1.2 in the fixture's Docker container, on 2026-10-02. The commands are the ones in `.github/workflows/ci.yml`:

```
dotnet build src/NServiceBus.Community.Mqtt.slnx --configuration Release
dotnet test src/NServiceBus.Community.Mqtt.TransportTests --configuration Release --no-build --logger "trx;LogFileName=transport-tests.trx" --results-directory TestResults
dotnet test src/NServiceBus.Community.Mqtt.AcceptanceTests --configuration Release --no-build --logger "trx;LogFileName=acceptance-tests.trx" --results-directory TestResults
```

Failures are expected at this point. The counts below are the runner's own summary lines, and match the `<Counters>` element of each `.trx`.

| Project | Total | Passed | Failed | Ignored | Duration |
|---|---|---|---|---|---|
| `NServiceBus.Community.Mqtt.Tests` (unit) | 129 | 129 | 0 | 0 | under 1 s |
| `NServiceBus.Community.Mqtt.TransportTests` | 83 | 39 | 1 | 43 | 8 s |
| `NServiceBus.Community.Mqtt.AcceptanceTests` | 166 | 103 | 13 | 50 | 8 m 30 s |

The transport project's 83 tests are 77 suite tests (33 passed, 1 failed, 43 ignored) and 6 of this repository's own broker and cleaner tests (all passed). `When_publishing_to_scaled_out_subscribers` is not in the acceptance total: the run-settings filter removes it (see `src/KNOWN-EXCLUSIONS.md`).

## Ignored tests

Every ignored test is ignored by a suite gate, none by a filter.

- **Transport tests (43):** the `SendsAtomicWithReceive` and `TransactionScope` cases. The transport declares only `None` and `ReceiveOnly`, so the suite's transaction-mode check ignores them.
- **Acceptance tests (50):**

| Gate | Count |
|---|---|
| Requires delayed delivery (`SupportsDelayedDelivery = false`) | 16 |
| Requires message-driven publish/subscribe (the transport has native pub/sub) | 16 |
| Requires DTC transactions (`SupportsDtc = false`) | 9 |
| Requires an outbox (`SupportsOutbox = false`, the suite's persistence has none) | 7 |
| Requires cross-queue transactions (`SupportsCrossQueueTransactions = false`) | 2 |

## Failing tests, grouped by cause

### Transport tests (1)

| Cause | Tests |
|---|---|
| The pump logs a handler failure at Error level. The suite requires that a transport logs nothing above Info (the failure is for NServiceBus core to log). Fixed by the recoverability rewrite (6.3). | `When_on_error_throws.Should_invoke_critical_error_and_retry(ReceiveOnly)` |

Most other transport tests pass at this point, including the stop and cancellation ones. That says little: with a timing-dependent pump they can pass without the behaviour being right (`StopReceive` returns at once and its cancellation registration is disposed immediately). The MQTT-specific tests in sections 6 to 8 cover those behaviours directly.

### Acceptance tests (13)

| Cause | Tests | Fixed by |
|---|---|---|
| `Unsubscribe` is a no-op, so a handler keeps receiving events after it unsubscribed | `Routing.NativePublishSubscribe.When_unsubscribing_from_event.Should_no_longer_receive_event` (expected 1, was 4) | 8.1 |
| Events are published to `events/{ShortTypeName}` and not along the type hierarchy, so subscribers to a base type or an interface never get the event (all timeouts) | `Routing.NativePublishSubscribe.When_subscribing_to_a_base_event.Both_base_and_specific_events_should_be_delivered`<br>`Routing.NativePublishSubscribe.MultiSubscribeToPolymorphicEvent.Both_events_should_be_delivered`<br>`Routing.When_publishing_an_event_implementing_two_unrelated_interfaces.Event_should_be_published_using_instance_type`<br>`Sagas.When_started_by_base_event_from_other_saga.Should_start_the_saga_when_set_up_to_start_for_the_base_event`<br>`Versioning.When_multiple_versions_of_a_message_is_published.Should_deliver_is_to_both_v1_and_vX_subscribers` | 8.2, 8.3 |
| Purge on startup is not implemented (timeout) | `Feature.When_purging_queues.Should_purge_before_FeatureStartupTasks` | 6.7 |
| **The transport declares no time-to-be-received support, and these tests use it.** The 8.1.6 suite has no capability gate for TTBR, so core fails them at endpoint startup with "Messages with TimeToBeReceived found but the selected transport does not support this type of restriction". See below. | `TimeToBeReceived.When_TimeToBeReceived_has_expired.Message_should_not_be_received`<br>`TimeToBeReceived.When_TimeToBeReceived_has_expired_convention.Message_should_not_be_received`<br>`TimeToBeReceived.When_TimeToBeReceived_has_not_expired.Message_should_be_received`<br>`TimeToBeReceived.When_TimeToBeReceived_used_with_unobtrusive_mode.Message_should_not_be_received`<br>`Audit.When_auditing_message_with_TimeToBeReceived.Should_not_honor_TimeToBeReceived_for_audit_message`<br>`Recoverability.When_message_with_TimeToBeReceived_fails.Should_not_honor_TimeToBeReceived_for_error_message` | **needs a decision** |

### Decision: the six TTBR tests

**Resolved on 2026-10-02: the user chose option 1, support TTBR.** The transport now declares `supportsTTBR` and sets the MQTT 5 message expiry interval (design D3). The section below is the question as it was put.

### The question: the six TTBR tests

Design D3 and the `transport-public-api` spec declare TTBR unsupported, and the design lists it as a non-goal. The acceptance suite has no `Requires` gate for it, and the spec only allows a test to be skipped through a suite gate or an approved exclusion. So these six tests cannot pass with the current declaration, and none of them can be excluded without the user's approval (task 9.2).

There are two ways out, and the choice is the user's:

1. **Support TTBR.** MQTT 5 has a native per-message expiry (the Message Expiry Interval property), and a broker drops an expired message, including one queued for an offline session. Declare `supportsTTBR: true` and set the expiry on publish. This changes D3, the `transport-public-api` capability requirement and the README's capability table, and it needs a check on Mosquitto that queued messages really expire (the spike did not cover that).
2. **Record the six tests as approved exclusions** in `src/KNOWN-EXCLUSIONS.md` and the run-settings filter, with the limitation "no TTBR".

Nothing in sections 6 to 8 depends on this. It is raised again at task 9.2, before any of the six is excluded.

## After (task 9.1)

The reworked transport, on Mosquitto 2.1.2 in the fixture's Docker container, on 2026-10-02. The counts are the `<Counters>` element of each `.trx`.

| Project | Total | Passed | Failed | Ignored | Duration |
|---|---|---|---|---|---|
| `NServiceBus.Community.Mqtt.Tests` (unit) | 129 | 129 | 0 | 0 | under 1 s |
| `NServiceBus.Community.Mqtt.TransportTests` | 129 | 86 | **0** | 43 | 1 m 6 s |
| `NServiceBus.Community.Mqtt.AcceptanceTests` | 166 | 116 | **0** | 50 | 44 s |

The transport project's 129 tests are the 77 suite tests (the same as in the baseline) and 52 of this repository's own: 6 broker and cleaner tests, and 46 MQTT-specific tests for receiving, dispatching, queue declaration and publish/subscribe.

### Ignored transport tests (43)

Every one is ignored by the suite's own transaction-mode check. None is filtered out, and none is ignored by this repository. The transport declares `None` and `ReceiveOnly` only (design D3), and these are the cases for the modes it does not declare.

| Reason the suite gives | Count |
|---|---|
| Only relevant for transports supporting `SendsAtomicWithReceive` or higher | 20 |
| Only relevant for transports supporting `TransactionScope` or higher | 23 |

### Ignored acceptance tests (50)

Every one is ignored by one of the suite's own `Requires.*` gates, the same 50 as in the baseline. None is filtered out.

| Gate | Count |
|---|---|
| Requires message-driven publish/subscribe (this transport has native pub/sub) | 16 |
| Requires delayed delivery (`SupportsDelayedDelivery = false`) | 16 |
| Requires DTC transactions (`SupportsDtc = false`) | 9 |
| Requires an outbox (`SupportsOutbox = false`, the suite's persistence has none) | 7 |
| Requires cross-queue transactions (`SupportsCrossQueueTransactions = false`) | 2 |

### Excluded acceptance tests

One test is filtered out by `acceptance.runsettings` and recorded in `src/KNOWN-EXCLUSIONS.md`: `When_publishing_to_scaled_out_subscribers` (no scale-out, approved by the user). The `.trx` does not contain it, and the message-driven scale-out tests (`Pub_to_scaled_out_subs` and two others) are in the 50 ignored above. Reviewing the 116 that ran found no other test whose purpose is several instances sharing one queue: none failed with a takeover, and so no further exclusion was needed. The six TTBR tests that failed in the baseline pass now that the transport supports TTBR (see the decision above).

### What the acceptance run found

Running the whole suite found three problems that the transport tests could not, each fixed and covered by an MQTT-specific test:

- **Concurrent startup.** The suite starts all endpoints of a test at once, and they all declare the same holder sessions for `error` and `audit`. The broker lets one client use a client ID at a time, so the declarations displaced each other. A declaration that is cut short is now repeated after a random pause (`Should_declare_a_shared_sending_address_when_many_endpoints_start_at_the_same_moment`).
- **Purge on startup.** `When_purging_queues` showed that NServiceBus runs startup tasks after the transport is initialized and before it receives, so a purge when the pump first connects deleted the message a startup task had sent. The purge now happens when the transport is initialized (design D4).
- **MQTTnet disposal.** Disposing an MQTTnet client that the broker has just disconnected can throw `ObjectDisposedException` from MQTTnet's own cleanup, so every client is now disposed through a helper that tolerates it.
