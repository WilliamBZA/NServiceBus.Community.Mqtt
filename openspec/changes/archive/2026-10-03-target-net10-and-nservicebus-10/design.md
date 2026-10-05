# Design

## Context

See `proposal.md` for the motivation, and `specs/transport-project-structure/spec.md` for the platform requirement.

**Current state** (read from the repository):
- `src/Directory.Build.props` sets `TargetFramework` to `net8.0` and `LangVersion` to `12.0` for all four projects. `global.json` already pins SDK `10.0.100` with `rollForward: latestFeature`, and the machine has SDK 10.0.401 and runtime 10.0.12.
- The production project references `NServiceBus` 8.1.6 and `MQTTnet` 4.3.3.952, and treats warnings as errors. The test projects use the 8.1.6 suite Sources and `NServiceBus.AcceptanceTesting` 8.1.6, NUnit 3.14.0, NUnit3TestAdapter 5.0.0 and Microsoft.NET.Test.Sdk 17.14.1. `Particular.Approvals` 2.0.1 (from the Particular feed), `PublicApiGenerator` 11.5.4 and `Testcontainers` 4.15.0 complete the set.
- CI (`.github/workflows/ci.yml`) installs the 8.0 runtime and the 10.x SDK.
- The change `restructure-as-standard-transport` is complete (49/49 tasks) but **not archived**, so `openspec/specs/` is empty. This change's delta adds to the `transport-project-structure` capability that change defines.

**Constraints from NServiceBus 10.2.9** (read from the published nuspecs and from the approved API files in the `Particular/NServiceBus` repository at tags `8.1.6` and `10.2.9`):
- `NServiceBus` 10.2.9 has a single `net10.0` dependency group. It brings in `Microsoft.Extensions.Hosting` 10.0.9, `System.IO.Hashing` and `NServiceBus.MessageInterfaces` `[1.0.0, 2.0.0)`.
- The `*.TransportTests.Sources`, `*.AcceptanceTests.Sources` and `NServiceBus.AcceptanceTesting` 10.2.9 packages ship `contentFiles` only for `net10.0`, require `NServiceBus [10.2.9, 11.0.0)`, and require NUnit `[4.6.1, 5.0.0)`. NUnit 5.0.0 exists but is outside that range.
- Changes in the transport seam that affect this repository:
  - `TransportDefinition.ToTransportAddress(QueueAddress)` is **removed**. (It was obsolete as a warning in 8, an error in 9, and is gone in 10.) `MqttTransport` overrides it with `[Obsolete]`, and `MqttAddressTests` calls it under `#pragma warning disable CS0618`. `TransportInfrastructure.ToTransportAddress` is unchanged.
  - `TransportInfrastructure.Dispatcher` and `Receivers` now have `protected set`. `MqttTransportInfrastructure` assigns them from inside the derived class, so this still compiles.
  - `MessageContext` and `ErrorContext` gain constructor overloads that take `ReceiveProperties`. The overloads the pump calls today are still there and are not obsolete. `ErrorContext.Message` is now obsolete as a warning. The production code doesn't read it, but the suite sources or our harness might.
  - `HostSettings.CoreSettings` is now nullable, and `IsRawMode`, `ServiceProvider` and `SupportsDependencyInjection` are new. The constructor the transport-test harness calls is unchanged. The production code doesn't read `CoreSettings`.
  - `TransportDefinition` gains `ConfigureServices` / `ConfigureServicesCore` / `EnableEndpointFeature<T>`. These are optional and unused here.
- The test adapter interfaces haven't changed in ways that affect us. `IConfigureTransportInfrastructure` gains default-implemented `GetInputQueueName` / `GetErrorQueueName`, whose defaults keep the `{testName}{transactionMode}` naming that the broker cleaner expects. Error queues become `{testName}{mode}.error`. `IConfigureEndpointTestExecution` and `ITestSuiteConstraints` have the same members. `TestSuiteConstraints.Current` is now a property, and our partial class doesn't declare it.
- The 10.2.9 acceptance suite has tests that 8.1.6 didn't have, for example `Registrations/*`, `Pipeline/When_sending_record_struct_messages` and `Serialization/When_dynamic_loading_is_disabled`. `When_publishing_to_scaled_out_subscribers` still exists under the same name, so the current run-settings filter still applies.

## Goals / Non-Goals

**Goals:**
- One target framework, `net10.0`, set once in `Directory.Build.props`.
- The production code compiles warning-free against NServiceBus 10.2.9, with no new `#pragma` suppressions except where an upstream API leaves no choice. Each one gets a comment.
- The unmodified 10.2.9 transport and acceptance suites pass against Mosquitto 2.x, apart from approved exclusions.
- The public API changes only by losing `ToTransportAddress`.

**Non-Goals:**
- MQTTnet 5. It reworks the client API (`MqttFactory` → `MqttClientFactory`, among other changes) and is a separate change. MQTTnet 4.3.3.952 ships builds up to `net7.0` plus `netstandard2.1`. `net10.0` resolves the `net7.0` build, which is what `net8.0` uses today.
- Using NServiceBus 10 features: `ReceiveProperties`, `ConfigureServicesCore`, and the DI-aware `HostSettings`. The transport keeps the behavior it has.
- Multi-targeting. The user chose `net10.0` only.
- Changing the wire format, topics, address mapping, session handling or any other transport behavior.
- Bumping the package version. 2.0.0 has not shipped, so it stays 2.0.0 and its notes say it needs NServiceBus 10.

## Decisions

### D1. NServiceBus 10.2.9 rather than 9.x
The user asked for an NServiceBus upgrade together with `net10.0`. NServiceBus 10 is the major built for .NET 10, and its suite packages ship `net10.0` sources. NServiceBus 9 targets `net8.0`, so it would add a major version without getting the platform aligned. A plain `Version="10.2.9"` becomes the open dependency `>= 10.2.9` in the package, so a future NServiceBus 11 would satisfy it without anyone checking. **Choice:** reference the range `Version="[10.2.9, 11.0.0)"`, so the package's dependency says what the spec says. The alternative, a plain minimum version, is rejected for that reason.

### D2. Remove `MqttTransport.ToTransportAddress` instead of keeping it as a plain method
With no base member to override, the method could stay as a new public, non-override method that keeps the old signature. That keeps an API that NServiceBus deliberately took off transport definitions and that no caller in NServiceBus 10 uses. 2.0.0 is already a breaking release. **Choice:** remove it, and update the approval file. The address mapping is still covered by unit tests on `MqttAddress` and on `MqttTransportInfrastructure.ToTransportAddress`. The test `Should_use_the_same_mapping_in_the_transport_definition` becomes a check through the infrastructure, or is deleted if the existing infrastructure tests already cover it.

### D3. Keep calling the existing `MessageContext` / `ErrorContext` overloads
The overloads that take `ReceiveProperties` exist so transports can expose native receive metadata. MQTT 5 user properties are already carried inside the wrapped message headers, and NServiceBus doesn't need anything else from us. **Choice:** don't change the pump. Revisit only if the analyzers or an obsoletion force it.

### D4. Language version 14 via the SDK default
Removing `LangVersion` would track the SDK. Pinning it keeps builds reproducible when a future SDK changes the default. The repository already pins it. **Choice:** set `<LangVersion>14.0</LangVersion>`.

### D5. Test package versions
- NUnit: **4.6.1**. That's the floor of the suites' `[4.6.1, 5.0.0)` range, and pinning the floor matches what the suites were built against. The repository's tests use only the constraint model (`Assert.That`, `Assert.Throws`, `Assert.Multiple`) and no classic asserts, so the NUnit 4 removal of `Assert.AreEqual` and similar methods doesn't affect them. A grep found no NUnit 3 format-string overloads (`Assert.That(x, c, "… {0}", arg)`), which NUnit 4 also removed.
- NUnit3TestAdapter: **6.3.0** (latest, supports NUnit 4, brings the Microsoft.Testing.Platform VSTest bridge). Fallback: if `dotnet test` discovery or the `.runsettings` `TestCaseFilter` behaves differently under 6.x, use 5.x, which also supports NUnit 4. This is checked by running the acceptance project and confirming the excluded test does not run.
- Microsoft.NET.Test.Sdk: **18.10.1** (latest).
- `Particular.Approvals` 2.0.1 (no NUnit dependency, `net8.0` asset usable from `net10.0`), `PublicApiGenerator` 11.5.4, `Testcontainers` 4.15.0 and `Particular.Analyzers` 2.1.4 are already the latest. They stay as they are.

### D6. Suppressions in the test projects
NServiceBus 10 ships its own analyzers, and the suite sources may set off rules that the 8.1.6 sources didn't. Test projects don't treat warnings as errors, so these are only noise. They are handled as they are today: add a rule to the test project's `NoWarn` only if it fires **in the suite sources**, and never for our own files. Rules that 10.2.9 no longer sets off are removed from the list, so it stays accurate.

### D7. New acceptance tests that fail
The exclusion policy from `restructure-as-standard-transport` still applies. A failing new test is first treated as a transport bug and fixed. Only a test that the transport can't pass because of a documented limitation is proposed for exclusion. Exclusion needs the user's explicit approval (unless it is a pre-approved scale-out test), and the test is then recorded in `src/KNOWN-EXCLUSIONS.md` and in `acceptance.runsettings`. `KnownExclusionsTests` keeps the two in sync.

### D8. Archive order
`openspec/specs/transport-project-structure/spec.md` does not exist until `restructure-as-standard-transport` is archived. Archiving this change first would create that main spec with only this requirement and a placeholder purpose. **Choice:** archive `restructure-as-standard-transport` before this change. This affects only the OpenSpec bookkeeping, not the implementation, so implementation can start first.

## Risks / Trade-offs

- [NServiceBus 10 changes runtime behavior that the transport relies on, such as startup ordering, installers or the purge-on-startup timing] → The unmodified 10.2.9 suites plus our broker-backed tests are the safety net. Each failure is traced to the actual cause before anything changes. Nothing is fixed by editing suite sources.
- [MQTTnet 4.3.3 on .NET 10 behaves differently from .NET 8, for example in sockets or TLS defaults] → The full broker-backed suite runs on .NET 10 in CI. If a real incompatibility shows up, MQTTnet 4.3.x patch upgrades (latest 4.3.7.1207) are within the non-goal. MQTTnet 5 is not.
- [NUnit3TestAdapter 6.x handles `.runsettings` filters differently] → Mitigated in D5 by checking that the excluded test does not run, with 5.x as the fallback.
- [The acceptance suite runs longer because 10.2.9 has more tests] → CI's 30-minute timeout is checked against the measured run time. If the run gets close to the limit, the timeout is raised. Tests are not trimmed.
- [Consumers on .NET 8 or NServiceBus 8/9 lose a supported path to 2.0] → Accepted by the user. The README says so plainly, and 1.0.0 stays on nuget.org.
- [Bundling the framework move and the NServiceBus major in one change makes failures harder to bisect] → Tasks are ordered so the solution builds on `net10.0` with NServiceBus 8.1.6 first (the 8.1.6 suite sources resolve their `net7.0` content on `net10.0`), and the NServiceBus upgrade follows as a separate step. Each step is verified on its own.

## Migration Plan

1. Retarget to `net10.0` with no package changes, and run the full suite (8.1.6 on .NET 10).
2. Move to NServiceBus 10.2.9 and its suites with NUnit 4. Fix compile errors and the public API, then run the full suite.
3. Update CI and the README.

Rollback is reverting the commit. Nothing in this change touches broker state or the wire format.
