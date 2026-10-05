namespace NServiceBus.Community.Mqtt.Tests;

using System.Globalization;
using System.Xml.Linq;

/// <summary>
/// Every acceptance test that is excluded must be both filtered out in the run settings and recorded, with its approval, in
/// src/KNOWN-EXCLUSIONS.md. These tests keep the two in step, so an exclusion cannot be added quietly.
/// </summary>
[TestFixture]
public class KnownExclusionsTests
{
    [Test]
    public void Should_list_the_same_tests_in_the_run_settings_filter_and_in_the_record()
    {
        var filtered = ReadFilteredTests();
        var recorded = ReadRecord().Select(row => row.Test).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(filtered, Is.Not.Empty, "the run settings filter excludes nothing");
            Assert.That(filtered.Except(recorded), Is.Empty, "excluded by the run settings filter, but not recorded in KNOWN-EXCLUSIONS.md");
            Assert.That(recorded.Except(filtered), Is.Empty, "recorded in KNOWN-EXCLUSIONS.md, but not excluded by the run settings filter");
            Assert.That(recorded, Is.Unique, "recorded more than once");
        });
    }

    [Test]
    public void Should_record_the_limitation_the_approval_and_the_date_of_every_exclusion()
    {
        foreach (var (test, limitation, approvedBy, date) in ReadRecord())
        {
            Assert.Multiple(() =>
            {
                Assert.That(limitation, Is.Not.Empty, $"{test}: the limitation");
                Assert.That(approvedBy, Is.Not.Empty, $"{test}: who approved it");
                Assert.That(DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), Is.True, $"{test}: the date '{date}' is not yyyy-MM-dd");
            });
        }
    }

    [Test]
    public void Should_exclude_the_scale_out_test_that_needs_competing_consumers()
    {
        Assert.That(ReadFilteredTests(), Does.Contain("When_publishing_to_scaled_out_subscribers"));
    }

    static List<string> ReadFilteredTests()
    {
        var runSettings = Path.Combine(FindSourceDirectory(), "NServiceBus.Community.Mqtt.AcceptanceTests", "acceptance.runsettings");
        var filter = XDocument.Load(runSettings).Root?.Element("RunConfiguration")?.Element("TestCaseFilter")?.Value
            ?? throw new InvalidOperationException($"{runSettings} has no RunConfiguration/TestCaseFilter.");

        const string ExcludeTerm = "FullyQualifiedName!~";

        return filter.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.StartsWith(ExcludeTerm, StringComparison.Ordinal)
                ? term[ExcludeTerm.Length..]
                : throw new InvalidOperationException($"Unsupported term '{term}' in the run settings filter. Exclusions must be written as {ExcludeTerm}<test class name>, separated by '&'."))
            .ToList();
    }

    static List<(string Test, string Limitation, string ApprovedBy, string Date)> ReadRecord()
    {
        var file = Path.Combine(FindSourceDirectory(), "KNOWN-EXCLUSIONS.md");

        return File.ReadAllLines(file)
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Trim('|').Split('|', StringSplitOptions.TrimEntries))
            .Select(cells => cells.Length == 4
                ? (cells[0].Trim('`'), cells[1], cells[2], cells[3])
                : throw new InvalidOperationException($"A row of KNOWN-EXCLUSIONS.md does not have the columns test, limitation, approved by and date: {string.Join(" | ", cells)}"))
            .ToList();
    }

    // The tests run from <src>/<project>/bin/<configuration>/<framework>
    static string FindSourceDirectory()
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
