# Spec Delta

## ADDED Requirements

### Requirement: Supported platform
The package SHALL target .NET 10 (`net10.0`) and no other framework. It SHALL depend on NServiceBus 10, starting at 10.2.9 and below 11.0.0. All test projects SHALL target the same framework as the package, and SHALL run the standard transport and acceptance suites published for that NServiceBus version. Building, testing and CI SHALL need only a .NET 10 SDK, with no other .NET runtime. The README SHALL state the supported .NET and NServiceBus versions.

#### Scenario: Package targets only .NET 10
- **WHEN** the production project is packed
- **THEN** the package has assemblies only under `lib/net10.0/`, and its dependencies are NServiceBus 10.2.9 or later below 11.0.0, and MQTTnet

#### Scenario: Application on an older framework
- **WHEN** an application targeting `net8.0` adds the package
- **THEN** NuGet restore reports that the package is not compatible with the application's framework

#### Scenario: Endpoint on an older NServiceBus major version
- **WHEN** an application that references NServiceBus 8 or 9 adds the package
- **THEN** NuGet restore reports a version conflict, or upgrades NServiceBus to 10, instead of silently running against an older major version

#### Scenario: Tests run with only the .NET 10 SDK
- **WHEN** a contributor or CI runner with only the .NET 10 SDK installed builds the solution and runs all three test projects
- **THEN** the build succeeds and the tests run, without asking for any other runtime

#### Scenario: Suites match the NServiceBus version
- **WHEN** the test projects' package references are inspected
- **THEN** the transport and acceptance suite packages have the same version as the NServiceBus package the production project references

#### Scenario: README names the platform
- **WHEN** a reader opens the README
- **THEN** it states that the package targets .NET 10 and NServiceBus 10, and the test prerequisites list only the .NET 10 SDK and Docker
