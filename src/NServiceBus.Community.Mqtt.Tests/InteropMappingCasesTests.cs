namespace NServiceBus.Community.Mqtt.Tests;

using Interop;
using NServiceBus.Transport.Mqtt;

/// <summary>
/// Runs the cases in <c>src/Interop/MappingCases.cs</c> against the .NET transport. The device unit tests run the same cases against the device
/// package, so a rule that drifts on either side fails a test.
/// </summary>
[TestFixture]
public class InteropMappingCasesTests
{
    static IEnumerable<TestCaseData> Addresses() =>
        MappingCases.Addresses.Select(c => new TestCaseData(c).SetArgDisplayNames(c.Address is null ? "(null)" : $"'{c.Address}'"));

    static IEnumerable<TestCaseData> ClientIds() =>
        MappingCases.ClientIds.Select(c => new TestCaseData(c).SetArgDisplayNames($"'{c.Topic}'"));

    static IEnumerable<TestCaseData> EventTopics() =>
        MappingCases.EventTopics.Select(c => new TestCaseData(c).SetArgDisplayNames(c.EventType.FullName!));

    static IEnumerable<TestCaseData> Hierarchies() =>
        MappingCases.Hierarchies.Select(c => new TestCaseData(c).SetArgDisplayNames(c.EventType.FullName!));

    static IEnumerable<TestCaseData> EnclosedMessageTypeCases() =>
        MappingCases.EnclosedMessageTypes.Select(c => new TestCaseData(c).SetArgDisplayNames($"'{c.Header}'"));

    [TestCaseSource(nameof(Addresses))]
    public void Address_maps_to_the_expected_topic_or_is_rejected(AddressCase mappingCase)
    {
        if (mappingCase.Topic is null)
        {
            Assert.That(() => MqttAddress.ToTopic(mappingCase.Address), Throws.TypeOf<ArgumentException>());
        }
        else
        {
            Assert.That(MqttAddress.ToTopic(mappingCase.Address), Is.EqualTo(mappingCase.Topic));
        }
    }

    [TestCaseSource(nameof(ClientIds))]
    public void Queue_topic_maps_to_the_expected_client_id_or_is_rejected(ClientIdCase mappingCase)
    {
        if (mappingCase.ClientId is null)
        {
            Assert.That(() => MqttClientId.ForQueue(mappingCase.Topic), Throws.TypeOf<ArgumentException>());
        }
        else
        {
            Assert.That(MqttClientId.ForQueue(mappingCase.Topic), Is.EqualTo(mappingCase.ClientId));
        }
    }

    [TestCaseSource(nameof(EventTopics))]
    public void Event_type_maps_to_the_expected_topic(EventTopicCase mappingCase)
    {
        Assert.That(EventTopic.ToTopic(mappingCase.EventType), Is.EqualTo(mappingCase.Topic));
    }

    [TestCaseSource(nameof(Hierarchies))]
    public void Event_type_is_published_as_the_expected_types_in_order(HierarchyCase mappingCase)
    {
        var fullNames = EventTypeHierarchy.Enumerate(mappingCase.EventType).Select(type => type.FullName).ToArray();

        Assert.That(fullNames, Is.EqualTo(mappingCase.FullNames));
    }

    [TestCaseSource(nameof(EnclosedMessageTypeCases))]
    public void Enclosed_message_types_header_yields_the_expected_names(EnclosedMessageTypesCase mappingCase)
    {
        Assert.That(EnclosedMessageTypes.Names(mappingCase.Header).ToArray(), Is.EqualTo(mappingCase.Names));
    }
}
