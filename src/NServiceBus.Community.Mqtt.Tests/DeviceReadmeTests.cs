namespace NServiceBus.Community.Mqtt.Tests;

using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// The device section of the README says which nanoFramework package versions the device package needs, shows code from the sample app, and lists
/// the defaults of the configuration. These tests keep the README and the code in step, so the documentation cannot drift quietly.
/// </summary>
[TestFixture]
public partial class DeviceReadmeTests
{
    [Test]
    public void Should_list_the_versions_of_the_nuspec_dependencies_in_the_readme()
    {
        var fromNuspec = ReadNuspecDependencies();
        var fromReadme = ReadReadmeVersionTable();

        Assert.Multiple(() =>
        {
            Assert.That(fromNuspec, Is.Not.Empty, "the .nuspec lists no dependencies");
            Assert.That(fromReadme, Is.EqualTo(fromNuspec), "the version table in the README (left) and the dependencies of the .nuspec (right) differ");
        });
    }

    [Test]
    public void Should_pin_every_nuspec_dependency_to_one_exact_version()
    {
        foreach (var (package, version) in ReadNuspecDependencies())
        {
            Assert.That(version, Does.Match(@"^\d+\.\d+\.\d+$"), package);
        }
    }

    [Test]
    public void Should_pin_the_same_versions_in_packages_config()
    {
        var pinned = XDocument.Load(Path.Combine(DeviceProject, "packages.config")).Root!.Elements("package")
            .ToDictionary(package => (string)package.Attribute("id")!, package => (string)package.Attribute("version")!);

        foreach (var (package, version) in ReadNuspecDependencies())
        {
            Assert.That(pinned.GetValueOrDefault(package), Is.EqualTo(version), $"packages.config pins {package}");
        }
    }

    [Test]
    public void Should_show_code_in_the_device_section_that_the_sample_app_contains()
    {
        var sample = Normalize(string.Join("\n", SampleSources().Select(File.ReadAllText)));
        var snippets = DeviceSectionCodeBlocks().ToList();

        Assert.That(snippets, Is.Not.Empty, "the device section has no C# code");
        foreach (var snippet in snippets)
        {
            Assert.That(sample, Does.Contain(Normalize(snippet)), $"this README code is not in the sample app (src/NServiceBus.Community.NanoframeworkMQTT.Sample, src/Interop/InteropContracts.cs):\n{snippet}");
        }
    }

    [Test]
    public void Should_document_the_defaults_the_configuration_has()
    {
        var source = File.ReadAllText(Path.Combine(DeviceProject, "DeviceEndpointConfiguration.cs"));
        var documented = DeviceSection();

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("TimeSpan.FromSeconds(7 * 24 * 60 * 60)"), "the session expiry default is 7 days");
            Assert.That(documented, Does.Contain("| `SessionExpiry` | 7 days |"));

            Assert.That(source, Does.Contain("int immediateRetries = 5;"));
            Assert.That(documented, Does.Contain("| `ImmediateRetries` | 5 |"));

            Assert.That(source, Does.Contain("string errorQueue = \"error\";"));
            Assert.That(documented, Does.Contain("| `ErrorQueue` | `error` |"));

            Assert.That(source, Does.Contain("int maximumPacketSize = 16384;"));
            Assert.That(documented, Does.Contain("| `MaximumPacketSize` | 16384 bytes |"));

            Assert.That(source, Does.Contain("TimeSpan dispatchTimeout = TimeSpan.FromSeconds(10);"));
            Assert.That(documented, Does.Contain("| `DispatchTimeout` | 10 seconds |"));

            Assert.That(source, Does.Contain("TimeSpan stopDrainTimeout = TimeSpan.FromSeconds(10);"));
            Assert.That(documented, Does.Contain("| `StopDrainTimeout` | 10 seconds |"));
        });
    }

    [Test]
    public void Should_say_that_time_to_be_received_is_not_available_on_the_device_yet()
    {
        Assert.That(DeviceSection(), Does.Contain("**Not available yet.**"));
    }

    static List<(string Package, string Version)> ReadNuspecDependencies()
    {
        var nuspec = XDocument.Load(Path.Combine(DeviceProject, "NServiceBus.Community.NanoframeworkMQTT.nuspec"));

        return nuspec.Descendants().Where(element => element.Name.LocalName == "dependency")
            .Select(dependency => ((string)dependency.Attribute("id")!, ((string)dependency.Attribute("version")!).Trim('[', ']')))
            .OrderBy(dependency => dependency.Item1, StringComparer.Ordinal)
            .ToList();
    }

    static List<(string Package, string Version)> ReadReadmeVersionTable() =>
        DeviceSection().Split('\n')
            .Select(line => VersionRow().Match(line))
            .Where(match => match.Success)
            .Select(match => (match.Groups["package"].Value, match.Groups["version"].Value))
            .OrderBy(row => row.Item1, StringComparer.Ordinal)
            .ToList();

    [GeneratedRegex(@"^\|\s*`(?<package>nanoFramework\.[A-Za-z0-9.]+)`\s*\|\s*`(?<version>\d+\.\d+\.\d+)`\s*\|\s*$")]
    private static partial Regex VersionRow();

    // from the heading of the device section to the next heading of the same level
    static string DeviceSection()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md")).Replace("\r\n", "\n");
        const string Heading = "\n## Devices (.NET nanoFramework)\n";
        var start = readme.IndexOf(Heading, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "the README has no device section");

        var end = readme.IndexOf("\n## ", start + Heading.Length, StringComparison.Ordinal);
        return readme[start..(end < 0 ? readme.Length : end)];
    }

    static IEnumerable<string> DeviceSectionCodeBlocks()
    {
        var lines = DeviceSection().Split('\n');
        var block = new List<string>();
        var inBlock = false;

        foreach (var line in lines)
        {
            if (!inBlock && line.StartsWith("```csharp", StringComparison.Ordinal))
            {
                inBlock = true;
                block.Clear();
            }
            else if (inBlock && line.StartsWith("```", StringComparison.Ordinal))
            {
                inBlock = false;
                yield return string.Join("\n", block);
            }
            else if (inBlock)
            {
                block.Add(line);
            }
        }
    }

    static IEnumerable<string> SampleSources() =>
        Directory.GetFiles(Path.Combine(SourceDirectory, "NServiceBus.Community.NanoframeworkMQTT.Sample"), "*.cs")
            .Append(Path.Combine(SourceDirectory, "Interop", "InteropContracts.cs"));

    // lines compared by their text only: indentation and blank lines do not matter
    static string Normalize(string code) =>
        string.Join("\n", code.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));

    static string DeviceProject => Path.Combine(SourceDirectory, "NServiceBus.Community.NanoframeworkMQTT");

    static string RepositoryRoot => Directory.GetParent(SourceDirectory)!.FullName;

    // The tests run from <src>/<project>/bin/<configuration>/<framework>
    static string SourceDirectory
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "KNOWN-EXCLUSIONS.md")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException($"Could not find src/KNOWN-EXCLUSIONS.md above {AppContext.BaseDirectory}.");
        }
    }
}
