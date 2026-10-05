# transport-project-structure Specification

## Purpose

Defines the repository, solution, project and CI layout that makes this transport look and build like other NServiceBus transports. A contributor who knows one transport repo can navigate this one, and every change is verified by the same standard suites.

## Requirements

### Requirement: Standard repository layout
The repository SHALL keep all buildable code under `src/`, with one solution file in `src/` that includes the production project and the three test projects. The repository root SHALL contain only repo-level files: `README.md`, `LICENSE`, `.gitignore`, `.gitattributes`, `global.json`, `nuget.config`, and `.github/`. No `.cs` or `.csproj` file SHALL remain in the repository root.

#### Scenario: Clean clone builds
- **WHEN** a contributor clones the repository and runs `dotnet build` on the solution in `src/`
- **THEN** the production project and all three test projects build without extra manual steps

#### Scenario: Root is free of source
- **WHEN** the repository root is listed
- **THEN** it contains no source files or project files, only the repo-level files above and the `src/`, `openspec/` and `.github/` folders

### Requirement: Project and package naming
The production project, its assembly and its NuGet package SHALL all be named `NServiceBus.Community.Mqtt`. The test projects SHALL be named `NServiceBus.Community.Mqtt.Tests`, `NServiceBus.Community.Mqtt.TransportTests` and `NServiceBus.Community.Mqtt.AcceptanceTests`. Each project SHALL live in a folder of the same name directly under `src/`.

#### Scenario: Package identity is preserved
- **WHEN** the production project is packed
- **THEN** the package ID is `NServiceBus.Community.Mqtt` and the assembly file is `NServiceBus.Community.Mqtt.dll`

#### Scenario: Test projects follow the convention
- **WHEN** the `src/` folder is listed
- **THEN** it contains exactly the production project folder and the `.Tests`, `.TransportTests` and `.AcceptanceTests` folders, plus shared build files and the solution

### Requirement: Shared build configuration
Settings common to all projects (target framework, nullable context, language version, analyzers, warning policy) SHALL be defined once in shared build files under `src/`. Projects SHALL only declare what is specific to them.

#### Scenario: One place to change a common setting
- **WHEN** the target framework or analyzer set needs to change for every project
- **THEN** the change is made in the shared build file and applies to all projects without editing each project file

### Requirement: Self-starting Mosquitto test broker with an external override
Broker-backed test projects SHALL start a Mosquitto 2.x broker in a Docker container by default, using the repository's Mosquitto configuration, on a free host port, and SHALL stop and remove the container when the test run ends. Each broker-backed project SHALL start its own container. The only prerequisite SHALL be a running Docker engine with Linux containers; no broker needs to be installed. The configuration SHALL keep sessions across client disconnects and SHALL raise the queued-message limits above what the suites need. When the environment variables `MqttTransport_Server` and `MqttTransport_Port` are set, the tests SHALL connect to that existing broker and SHALL NOT start a container. The repository SHALL include the Mosquitto 2.x configuration and README instructions for running a broker by hand for that override.

#### Scenario: Default run needs only Docker
- **WHEN** a contributor runs the transport tests or acceptance tests with the environment variables unset, no broker installed and the Docker engine running
- **THEN** the tests start a Mosquitto container, connect to it, and remove it when the run ends

#### Scenario: Overriding the broker address
- **WHEN** `MqttTransport_Server` and `MqttTransport_Port` are set
- **THEN** the broker-backed tests connect to that address and do not start a container

#### Scenario: Projects do not share a broker
- **WHEN** the transport tests and the acceptance tests run at the same time with the environment variables unset
- **THEN** each project talks to its own broker, so they cannot see each other's sessions or queued messages

#### Scenario: Manual broker run is documented
- **WHEN** a contributor follows the README's instructions to run Mosquitto by hand with the repository's configuration
- **THEN** they get a broker the tests can use through the environment variables

### Requirement: Continuous integration
The repository SHALL have a GitHub Actions workflow that runs on pull requests and on pushes to the default branch. It SHALL run on a Linux runner with Docker, build the solution and run all three test projects once, with the broker-backed projects starting Mosquitto 2.x themselves. It SHALL fail if any test fails.

#### Scenario: Pull request is verified against Mosquitto
- **WHEN** a pull request is opened
- **THEN** the workflow builds the solution and runs the unit, transport and acceptance tests against a Mosquitto 2.x broker started by the tests, and reports the result on the pull request

### Requirement: README documents usage and testing
The root `README.md` SHALL describe what the transport is, how to install and configure it, how queues and events map to MQTT topics and sessions, which NServiceBus capabilities are and are not supported, and how to run the tests locally.

#### Scenario: Contributor can run tests from the README alone
- **WHEN** a contributor follows the README
- **THEN** they can start a broker, set the optional environment variables, and run the full test suite

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
