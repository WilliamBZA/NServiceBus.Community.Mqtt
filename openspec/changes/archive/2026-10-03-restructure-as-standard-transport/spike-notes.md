# Spike notes (section 1)

**Status: COMPLETE.** The spike ran on both brokers. The Mosquitto half ran on 2026-10-02, after Docker Desktop became available, and **superseded** the earlier "in-process only for now" decision (see the last section). **Gate (task 1.6): passed.** S1 passes on Mosquitto 2.x, so persistent-session retention works and the transport rework can proceed.

The spike is a throwaway console project outside the repo (session scratchpad) against `MQTTnet` 4.3.3.952, MQTT 5 throughout. It ran against two targets:
- **In-process broker**: MQTTnet's own server, `WithPersistentSessions(true)`, a raised `MaxPendingMessagesPerClient` (100000) and a free loopback port.
- **Mosquitto 2.1.2**: the `eclipse-mosquitto:2` image (digest `sha256:38c0da4f2ef8…`) in Docker, started with `docker run` with the committed `src/mosquitto/mosquitto.conf` mounted, port 1883 published, and removed afterwards.

## S4: API names (task 1.1) - PASS

Each API compiled and was exercised against the in-process broker, with server-side evidence. The client-side calls were exercised again, unchanged, against Mosquitto in S1 to S6.

| API | Evidence |
|---|---|
| `MqttServerOptionsBuilder.WithPersistentSessions(true)` | `EnablePersistentSessions=True` |
| `MqttServerOptionsBuilder.WithMaxPendingMessagesPerClient(n)` | `MaxPendingMessagesPerClient=100000` |
| `WithDefaultEndpointBoundIPAddress` + `WithDefaultEndpointPort` | started on `127.0.0.1:<free port>` |
| `MqttClientOptionsBuilder.WithProtocolVersion(MqttProtocolVersion.V500)` | server saw V500 |
| `WithCleanStart(false)` (sets `Options.CleanSession`) | server saw `CleanSession=False` |
| `WithSessionExpiryInterval(1234)` | server saw 1234 |
| `WithReceiveMaximum(7)` | server saw 7 |
| `MqttClientConnectResult.IsSessionPresent` | false on first connect, true on reconnect with `CleanStart=false` (both brokers) |
| `e.AutoAcknowledge = false` + `e.AcknowledgeAsync(ct)` | server saw 0 acks before the call, 1 after |
| `MqttClientDisconnectedEventArgs.Reason` | `AdministrativeAction` (152) for a server DISCONNECT; `SessionTakenOver` (142 = 0x8E) on takeover (both brokers) |
| `MqttServer.GetSessionsAsync()`, `MqttSessionStatus` (`Id`, `ExpiryInterval`, `PendingApplicationMessagesCount`, `DeleteAsync()`) | found the session with its expiry and pending count (in-process broker only; not needed now that tests use Mosquitto) |

Notes for later tasks:
- Subscribe with the extension `client.SubscribeAsync(MqttTopicFilter, ct)`; disconnect with `new MqttClientDisconnectOptions { Reason = NormalDisconnection }`.
- `ReceiveMaximum` is not a server option, only a connection-validation field.

## Results per broker

| Check | In-process broker | Mosquitto 2.1.2 |
|---|---|---|
| S1 persistent session retains 5 QoS 1 messages | **PASS** (`IsSessionPresent=true`, 5/5) | **PASS** (first connect `IsSessionPresent=false`, reconnect `true`, 5/5 in order) |
| S2 takeover reports `0x8E`, displaced client stays down | **PASS** (142 = 0x8E; second client keeps the session and the subscription) | **PASS** (142 = 0x8E; first stays disconnected; second inherits the subscription and gets the next publish) |
| S3 holder session `nsb.x.declared` does not displace `nsb.x` | **PASS** (live 3/3, holder 3/3) | **PASS** (live not displaced, live 3/3, holder `IsSessionPresent=true`, holder 3/3) |
| S4 API names | **PASS** (above) | n/a |
| S5 `CleanStart=true` deletes queued messages and subscriptions | **PASS** (session not present, 0 queued, 0 after a fresh publish) | **PASS** (`IsSessionPresent=false`, 0 queued delivered, 0 after a fresh publish) |
| S6 broker limits | **FAIL** on (b) only, see below | **PASS** on (a), (b) and (c) with the committed config; default config caps the queue at 1000, see below |
| X1 (extra) unacknowledged message is redelivered on session resume | **PASS** (1 redelivered; `Dup` flag false) | **PASS** (1 redelivered; `Dup` flag **true**) |
| X2 (extra) client IDs with `/`, space, unicode, 150+ chars, GUID | **PASS** | **PASS** (all 5 accepted) |
| X3 (extra) QoS 1 publish with no subscribers | **PASS**: `NoMatchingSubscribers`, `IsSuccess=true` | **PASS**: `NoMatchingSubscribers`, `IsSuccess=true` |

### S6 detail

| Sub-check | In-process broker | Mosquitto 2.1.2 |
|---|---|---|
| S6a offline queue cap, 1500 messages to an offline session | raised cap (100000): **PASS**, 1500/1500. Default cap: **FAIL**, 250/1500 (`DropOldestQueuedMessage`: first delivered was `m1251`) | committed config (`max_queued_messages 100000`): **PASS**, 1500/1500, first delivered `m1`. Default config (no `max_queued_messages`): **FAIL**, 1000/1500, first delivered `m1` (the **newest** messages are dropped and the oldest kept) |
| S6b `ReceiveMaximum=5`, 50 messages, no acknowledgement | **FAIL**: all 50 arrived before any acknowledgement; the in-process broker ignores `ReceiveMaximum` | **PASS**: 5 arrived before any acknowledgement, and 50/50 arrived once they were acknowledged |
| S6c session expiry | **PASS**: a 2 s session was gone after 8 s offline; a 600 s control survived with its message | **PASS**: the 2 s session was gone after 8 s offline (`IsSessionPresent=false`, nothing delivered); the 600 s control survived with its message |

## Findings that affect the design

1. **Persistent sessions behave as D4 assumes on Mosquitto 2.x.** Retention (S1), takeover with `0x8E` (S2), holder sessions that do not displace the live consumer (S3) and deletion by `CleanStart=true` (S5) all pass.
2. **`ReceiveMaximum` is honoured by Mosquitto** (S6b), so broker-side backpressure works as the design expects. The in-process broker ignores it, which is one more reason it is not the test target. Still to check in task 6.2: if the intake handler blocks when the channel is full, that must not stall MQTTnet's read loop and keep-alive.
3. **Session expiry is enforced over time on both brokers** (S6c), so the short `SessionExpiry` in the test adapters (D7) bounds how long a crashed run's sessions survive on a hand-started broker.
4. **Mosquitto's default offline queue cap is 1000 and it drops the newest messages silently.** The committed config raises it to 100000. This must go into the README: a broker with the default cap loses messages sent to an offline endpoint once the backlog passes 1000, and the sender is not told (the publish still succeeds).
5. **A publish with no subscribers is a success** (`NoMatchingSubscribers`, `IsSuccess=true`) on both brokers. The dispatcher's result-code check in 7.1 must treat `IsSuccess` as success, not only `Success`.
6. **Takeover hands the new client the old session**, including its subscriptions and queued messages (S2). This is consistent with D4, where a displaced consumer's queue passes to the new one.
7. **Unacknowledged messages are redelivered when the same session reconnects** (X1), and Mosquitto sets the `Dup` flag on the redelivery. `ReceiveOnly` acknowledgement semantics rely on this. The pump must not use `Dup` to decide whether a message is a duplicate to drop.

## Gate (task 1.6): passed

- S1 passes on Mosquitto 2.x, so the "stop and ask" condition does not trigger.
- Every one of S1 to S6 has an explicit PASS or FAIL on Mosquitto, recorded above. The one FAIL is the *default* Mosquitto config's queue cap (S6a), which the committed config fixes; with the committed config every S6 sub-check passes.
- The Mosquitto image used was `eclipse-mosquitto:2`, which resolved to Mosquitto 2.1.2.

## Superseded: decision on the missing Mosquitto results (user, 2026-10-02, earlier in the day)

This section is kept for the record. **It no longer applies.** When Docker was unavailable, the user chose to continue on the in-process broker alone, with tasks 1.2 to 1.6 left open and the suites to be verified on Mosquitto later in CI. Later the same day Docker Desktop was installed and the user moved the test broker to Mosquitto in Docker (see `proposal.md` and `design.md` D7). The spike was then run on Mosquitto as recorded above, and the in-process results are kept only as a comparison.
