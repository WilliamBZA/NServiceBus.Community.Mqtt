# Proposal

## Why

The transport targets `net8.0` and NServiceBus 8.1.6. Support for .NET 8 ends in November 2026, and NServiceBus 8 is two majors behind. NServiceBus 10 (current: 10.2.9) targets only `net10.0`. Package 2.0.0 has not shipped yet (nuget.org only has 1.0.0), so 2.0.0 can move to the current platform before anyone depends on the .NET 8 build. Moving later would mean another major version.

## What Changes

- **BREAKING**: The package targets `net10.0` only. `net8.0` is dropped, and .NET 8 apps can't use 2.0.0.
- **BREAKING**: The package depends on NServiceBus `[10.2.9, 11.0.0)` instead of 8.1.6. Endpoints on NServiceBus 8 or 9 can't use 2.0.0.
- **BREAKING (public API)**: `MqttTransport.ToTransportAddress(QueueAddress)` is removed, because NServiceBus 10 removed the abstract member it overrode. Address translation stays on the transport infrastructure, which NServiceBus reaches through `ITransportAddressResolver`. The `_` → `/` mapping does not change.
- All four projects target `net10.0` from the shared `src/Directory.Build.props`. The C# language version moves from 12 to 14, the .NET 10 default.
- The test projects move to the NServiceBus 10.2.9 suite packages (`NServiceBus.TransportTests.Sources`, `NServiceBus.AcceptanceTests.Sources`, `NServiceBus.AcceptanceTesting`). Those packages require NUnit `[4.6.1, 5.0.0)`, so the tests move from NUnit 3.14 to NUnit 4.6.1, with a matching test adapter and test SDK.
- The standard suites run unmodified again at the new version. A newly failing test that the transport can't pass is excluded only with the user's approval, under the existing exclusion policy.
- CI installs only the .NET 10 SDK. The extra 8.0 runtime is removed.
- The README states the new platform (.NET 10, NServiceBus 10) in the overview, in "What you need", and in the 1.x → 2.0 upgrade section.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `transport-project-structure`: adds a requirement naming the supported platform: target framework `net10.0` only, NServiceBus 10, and a .NET 10 SDK and runtime for builds, tests and CI with no other runtime. This capability is defined by the in-flight change `restructure-as-standard-transport` (complete, not yet archived), so the requirement is written as an addition to it.

## Impact

- **Build configuration**: `src/Directory.Build.props` (`TargetFramework`, `LangVersion`). `global.json` already pins the 10.x SDK and does not change.
- **Production project**: `NServiceBus` package reference 8.1.6 → 10.2.9. `MqttTransport.ToTransportAddress` is removed. The code may need other small fixes for NServiceBus 10 API changes and new analyzer findings, because the production project treats warnings as errors.
- **MQTTnet** stays on 4.3.3.952. It runs on .NET 10 through its `net7.0` build, the same one `net8.0` uses today. MQTTnet 5 changes its API, and that move is not part of this change.
- **Public API approval file**: `src/NServiceBus.Community.Mqtt.Tests/ApprovalFiles/APIApprovals.Approve.approved.txt` changes, because the `ToTransportAddress` override is removed.
- **Unit tests**: `MqttAddressTests.Should_use_the_same_mapping_in_the_transport_definition` calls the removed member and must instead check the mapping through the infrastructure's `ToTransportAddress`.
- **Test projects**: package versions (suite Sources, AcceptanceTesting, NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk), the `NoWarn` list for suite sources, and possibly `src/KNOWN-EXCLUSIONS.md` plus `acceptance.runsettings`.
- **CI**: `.github/workflows/ci.yml` (setup-dotnet versions, comment).
- **Docs**: `README.md`.
- **Consumers**: 2.0.0 needs .NET 10 and NServiceBus 10. Their endpoints must be upgraded to NServiceBus 10 first. The wire format, topics and sessions don't change, so a 2.0 endpoint can still exchange messages with a 1.x endpoint as the existing upgrade notes describe.
