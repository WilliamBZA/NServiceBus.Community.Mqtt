using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyTitle("NServiceBus.Community.NanoframeworkMQTT")]
[assembly: AssemblyDescription("An NServiceBus-compatible endpoint for .NET nanoFramework devices, over MQTT 5.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("NServiceBus.Community.NanoframeworkMQTT")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// The device unit tests run in an assembly called NFUnitTest, which is the name nanoFramework's test launcher loads.
[assembly: InternalsVisibleTo("NFUnitTest")]
