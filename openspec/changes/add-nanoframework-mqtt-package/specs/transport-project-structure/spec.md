# Spec Delta

## MODIFIED Requirements

### Requirement: Standard repository layout
The repository SHALL keep all buildable code under `src/`. `src/` SHALL hold two solution files:
- the .NET solution (`.slnx`), which includes the .NET transport project, its three test projects and the interop host
- the nanoFramework solution (`.sln`), which includes the device package project, its unit test projects and the sample device app

The repository root SHALL contain only repo-level files: `README.md`, `LICENSE`, `.gitignore`, `.gitattributes`, `global.json`, `nuget.config`, and `.github/`. No `.cs`, `.csproj` or `.nfproj` file SHALL remain in the repository root.

#### Scenario: Clean clone builds
- **WHEN** a contributor clones the repository and runs `dotnet build` on the .NET solution in `src/`
- **THEN** the .NET transport project, all three test projects and the interop host build without extra manual steps

#### Scenario: Clean clone builds the device package
- **WHEN** a contributor on Windows, with MSBuild and the nanoFramework build components installed, restores and builds the nanoFramework solution in `src/`
- **THEN** the device package project, its unit test projects and the sample device app build without extra manual steps

#### Scenario: Root is free of source
- **WHEN** the repository root is listed
- **THEN** it contains no source files or project files, only the repo-level files above and the `src/`, `openspec/` and `.github/` folders

### Requirement: Project and package naming
The .NET transport project, its assembly and its NuGet package SHALL all be named `NServiceBus.Community.Mqtt`. Its test projects SHALL be named `NServiceBus.Community.Mqtt.Tests`, `NServiceBus.Community.Mqtt.TransportTests` and `NServiceBus.Community.Mqtt.AcceptanceTests`. The device package project, its assembly and its NuGet package SHALL all be named `NServiceBus.Community.NanoframeworkMQTT`. The device unit test project SHALL be named `NServiceBus.Community.NanoframeworkMQTT.Tests`. A nanoFramework assembly can hold only so many strings, so the device unit tests MAY be split over a second project, `NServiceBus.Community.NanoframeworkMQTT.Tests.Endpoint`, which shares the folder of the first. The sample device app SHALL be named `NServiceBus.Community.NanoframeworkMQTT.Sample`, and the interop host `NServiceBus.Community.NanoframeworkMQTT.InteropHost`. Each project SHALL live in a folder of the same name directly under `src/`, except the second device unit test project. Source files shared by the device projects and the .NET projects SHALL live in `src/Interop/`, which contains no project.

#### Scenario: Package identity is preserved
- **WHEN** the .NET transport project is packed
- **THEN** the package ID is `NServiceBus.Community.Mqtt` and the assembly file is `NServiceBus.Community.Mqtt.dll`

#### Scenario: Device package identity
- **WHEN** the device package project is packed
- **THEN** the package ID is `NServiceBus.Community.NanoframeworkMQTT` and its assembly is `NServiceBus.Community.NanoframeworkMQTT`

#### Scenario: Test projects follow the convention
- **WHEN** the `src/` folder is listed
- **THEN** it contains exactly these entries:
  - the eight project folders named above
  - `Interop/` and `mosquitto/`
  - the shared build files and `KNOWN-EXCLUSIONS.md`
  - the two solutions

### Requirement: Shared build configuration
Settings common to all .NET projects SHALL be defined once in shared build files under `src/`. These settings are the target framework, nullable context, language version, analyzers and warning policy. The nanoFramework projects SHALL NOT pick up those settings. Projects SHALL only declare what is specific to them.

#### Scenario: One place to change a common setting
- **WHEN** the target framework or analyzer set needs to change for every .NET project
- **THEN** the change is made in the shared build file and applies to all .NET projects without editing each project file

#### Scenario: nanoFramework projects are unaffected
- **WHEN** the .NET target framework or analyzer set is changed in the shared build file
- **THEN** the nanoFramework projects build exactly as before

### Requirement: Continuous integration
The repository SHALL have a GitHub Actions workflow that runs on pull requests and on pushes to the default branch. It SHALL have two jobs:
- **Linux job.** It SHALL run on a Linux runner with Docker. It SHALL build the .NET solution, including the interop host, and run all three .NET test projects once. The broker-backed projects SHALL start Mosquitto 2.x themselves.
- **Windows job.** It SHALL build the nanoFramework solution, including the sample device app, run the device unit tests on the nanoCLR virtual device, and pack the device package.

The workflow SHALL fail if any build, test or pack step fails.

#### Scenario: Pull request is verified against Mosquitto
- **WHEN** a pull request is opened
- **THEN** the workflow builds the .NET solution and runs the unit, transport and acceptance tests against a Mosquitto 2.x broker started by the tests, and reports the result on the pull request

#### Scenario: Pull request verifies the device package
- **WHEN** a pull request is opened
- **THEN** the workflow builds the nanoFramework solution, runs the device unit tests on the nanoCLR virtual device, packs the device package, and reports the result on the pull request

### Requirement: README documents usage and testing
The root `README.md` SHALL describe the .NET transport: what it is, how to install and configure it, how queues and events map to MQTT topics and sessions, which NServiceBus capabilities are and are not supported, and how to run the tests locally. It SHALL also describe the device package:
- what it is and how to install and configure it on a device
- how to share message contracts between devices and .NET endpoints
- which features are and are not supported
- that messages are acknowledged on receipt, and what that means
- that every device needs its own endpoint name
- the maximum packet size and the dispatch timeout
- whether time to be received is available
- how to run the device unit tests
- how to run the manual board test with the interop host

#### Scenario: Contributor can run tests from the README alone
- **WHEN** a contributor follows the README
- **THEN** they can start a broker, set the optional environment variables, and run the full .NET test suite, and on Windows they can build the nanoFramework solution and run the device unit tests

#### Scenario: Device developer can get started from the README alone
- **WHEN** a device developer follows the README's device section
- **THEN** they can install the package, configure an endpoint, and exchange a message with a .NET endpoint

### Requirement: Supported platform
The .NET transport package SHALL target .NET 10 (`net10.0`) and no other framework. It SHALL depend on NServiceBus 10, starting at 10.2.9 and below 11.0.0. All .NET test projects and the interop host SHALL target the same framework as the package, and the test projects SHALL run the standard transport and acceptance suites published for that NServiceBus version. Building and testing the .NET solution, and the CI job that does so, SHALL need only a .NET 10 SDK, with no other .NET runtime. The device package SHALL target .NET nanoFramework and SHALL depend on exact versions of the nanoFramework packages it uses. Building it SHALL need Windows, MSBuild and the nanoFramework build components. The README SHALL state the supported .NET and NServiceBus versions, the nanoFramework package versions, and that the device firmware must match them.

#### Scenario: Package targets only .NET 10
- **WHEN** the .NET transport project is packed
- **THEN** the package has assemblies only under `lib/net10.0/`, and its dependencies are NServiceBus 10.2.9 or later below 11.0.0, and MQTTnet

#### Scenario: Application on an older framework
- **WHEN** an application targeting `net8.0` adds the .NET transport package
- **THEN** NuGet restore reports that the package is not compatible with the application's framework

#### Scenario: Endpoint on an older NServiceBus major version
- **WHEN** an application that references NServiceBus 8 or 9 adds the .NET transport package
- **THEN** NuGet restore reports a version conflict, or upgrades NServiceBus to 10, instead of silently running against an older major version

#### Scenario: Tests run with only the .NET 10 SDK
- **WHEN** a contributor or CI runner with only the .NET 10 SDK installed builds the .NET solution and runs all three .NET test projects
- **THEN** the build succeeds and the tests run, without asking for any other runtime

#### Scenario: Suites match the NServiceBus version
- **WHEN** the test projects' package references are inspected
- **THEN** the transport and acceptance suite packages have the same version as the NServiceBus package the .NET transport project references

#### Scenario: Device package dependencies are pinned
- **WHEN** the device package's dependencies are inspected
- **THEN** every nanoFramework dependency has an exact version, and the README lists the same versions

#### Scenario: README names the platform
- **WHEN** a reader opens the README
- **THEN** it states that the .NET transport targets .NET 10 and NServiceBus 10, that its test prerequisites are only the .NET 10 SDK and Docker, and which nanoFramework versions the device package needs
