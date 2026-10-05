// The wire-rule cases both sides check: addresses, client IDs, event topics, event type hierarchies and EnclosedMessageTypes parsing.
// The .NET unit tests run them against the .NET transport, and the device unit tests run them against the device package, so a drift on
// either side fails a test. Same subset as InteropContracts.cs: no generics, records, target-typed new, LINQ or nullable annotations.
// Non-ASCII strings are written with escape sequences, except the one character outside the basic plane, which is written as itself (see below).
// The file is UTF-8, and both compilers read it as such.

#nullable disable

using System;
using NServiceBus;

namespace Sales
{
    /// <summary>The nested event type of the topic cases. It is partial because the .NET tests add generic nested types to the same class.</summary>
    public partial class Container
    {
        public class Inner : IEvent
        {
        }
    }
}

namespace Interop
{
    /// <summary>An address and the topic it maps to, or <c>null</c> when the address must be rejected.</summary>
    public class AddressCase
    {
        public readonly string Address;
        public readonly string Topic;

        public AddressCase(string address, string topic)
        {
            Address = address;
            Topic = topic;
        }
    }

    /// <summary>A queue topic and the client ID of its consumer, or <c>null</c> when the topic must be rejected.</summary>
    public class ClientIdCase
    {
        public readonly string Topic;
        public readonly string ClientId;

        public ClientIdCase(string topic, string clientId)
        {
            Topic = topic;
            ClientId = clientId;
        }
    }

    public class EventTopicCase
    {
        public readonly Type EventType;
        public readonly string Topic;

        public EventTopicCase(Type eventType, string topic)
        {
            EventType = eventType;
            Topic = topic;
        }
    }

    /// <summary>An event type and the full names of the types it is published as, in order.</summary>
    public class HierarchyCase
    {
        public readonly Type EventType;
        public readonly string[] FullNames;

        public HierarchyCase(Type eventType, string[] fullNames)
        {
            EventType = eventType;
            FullNames = fullNames;
        }
    }

    /// <summary>An <c>NServiceBus.EnclosedMessageTypes</c> header value and the full names it holds, without the assembly parts.</summary>
    public class EnclosedMessageTypesCase
    {
        public readonly string Header;
        public readonly string[] Names;

        public EnclosedMessageTypesCase(string header, string[] names)
        {
            Header = header;
            Names = names;
        }
    }

    public static class MappingCases
    {
        public static readonly AddressCase[] Addresses = new AddressCase[]
        {
            new AddressCase("Sales", "Sales"),
            new AddressCase("Sales_Billing", "Sales/Billing"),
            new AddressCase("Sales/Billing", "Sales/Billing"),
            new AddressCase("a_b_c", "a/b/c"),
            new AddressCase("Gate_01", "Gate/01"),
            new AddressCase("NanoInterop_Device", "NanoInterop/Device"),
            new AddressCase("error", "error"),
            new AddressCase("Café_Ünï", "Café/Ünï"),
            new AddressCase("x.y-z", "x.y-z"),

            // rejected: empty or whitespace, the MQTT wildcards, and a leading '$'
            new AddressCase(null, null),
            new AddressCase("", null),
            new AddressCase(" ", null),
            new AddressCase(" \t ", null),
            new AddressCase("a+b", null),
            new AddressCase("+", null),
            new AddressCase("a#", null),
            new AddressCase("gate/#", null),
            new AddressCase("$SYS", null),
            new AddressCase("$Sales_Billing", null),
        };

        public static readonly ClientIdCase[] ClientIds = new ClientIdCase[]
        {
            new ClientIdCase("Sales", "nsb.Sales"),
            new ClientIdCase("Sales/Billing", "nsb.Sales/Billing"),
            new ClientIdCase("Gate/01", "nsb.Gate/01"),
            new ClientIdCase("a.b-c_d/E9", "nsb.a.b-c_d/E9"),

            // everything outside A-Z a-z 0-9 . - _ / becomes ~XX per UTF-8 byte, and '~' itself is escaped
            new ClientIdCase("a b", "nsb.a~20b"),
            new ClientIdCase("a:b", "nsb.a~3Ab"),
            new ClientIdCase("~", "nsb.~7E"),
            new ClientIdCase("a~b", "nsb.a~7Eb"),
            new ClientIdCase("Café", "nsb.Caf~C3~A9"),
            new ClientIdCase("€", "nsb.~E2~82~AC"),
            // the one character outside the basic plane is written as itself: nanoFramework's compiler turns the two escaped surrogate halves of
            // a literal into two replacement characters, but keeps a literal character as the four UTF-8 bytes it is
            new ClientIdCase("😀", "nsb.~F0~9F~98~80"),

            // rejected
            new ClientIdCase("", null),
            new ClientIdCase("  ", null),
        };

        public static readonly EventTopicCase[] EventTopics = new EventTopicCase[]
        {
            new EventTopicCase(typeof(Contracts.ValveOpened), "events/Contracts.ValveOpened"),
            new EventTopicCase(typeof(Contracts.ValveEvent), "events/Contracts.ValveEvent"),
            new EventTopicCase(typeof(Contracts.IAlarm), "events/Contracts.IAlarm"),
            new EventTopicCase(typeof(Contracts.PriceChanged), "events/Contracts.PriceChanged"),

            // the full name of a nested type holds a '+', which a topic cannot contain
            new EventTopicCase(typeof(Sales.Container.Inner), "events/Sales.Container.Inner"),
        };

        public static readonly HierarchyCase[] Hierarchies = new HierarchyCase[]
        {
            // the type itself, its base classes nearest first, then its interfaces in ordinal order; the markers and System types are left out
            new HierarchyCase(typeof(Contracts.ValveOpened), new string[] { "Contracts.ValveOpened", "Contracts.ValveEvent", "Contracts.IAlarm", "Contracts.IAudited" }),
            new HierarchyCase(typeof(Contracts.ValveEvent), new string[] { "Contracts.ValveEvent" }),
            new HierarchyCase(typeof(Contracts.PriceChanged), new string[] { "Contracts.PriceChanged" }),
            new HierarchyCase(typeof(Contracts.IAlarm), new string[] { "Contracts.IAlarm" }),
            new HierarchyCase(typeof(Sales.Container.Inner), new string[] { "Sales.Container+Inner" }),
        };

        public static readonly EnclosedMessageTypesCase[] EnclosedMessageTypes = new EnclosedMessageTypesCase[]
        {
            new EnclosedMessageTypesCase("Contracts.OpenValve", new string[] { "Contracts.OpenValve" }),

            // assembly-qualified
            new EnclosedMessageTypesCase(
                "Contracts.OpenValve, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                new string[] { "Contracts.OpenValve" }),

            // several entries, in order
            new EnclosedMessageTypesCase(
                "Contracts.ValveOpened, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.ValveEvent, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAlarm, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                new string[] { "Contracts.ValveOpened", "Contracts.ValveEvent", "Contracts.IAlarm" }),

            // a generic entry has commas of its own inside brackets, so the name ends at the first comma outside them
            new EnclosedMessageTypesCase(
                "Sales.Wrapper`1[[System.Int32, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]], Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAlarm",
                new string[] { "Sales.Wrapper`1[[System.Int32, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]", "Contracts.IAlarm" }),

            // whitespace is trimmed, and empty entries are skipped
            new EnclosedMessageTypesCase(" Contracts.A ; ;Contracts.B,Asm;", new string[] { "Contracts.A", "Contracts.B" }),

            new EnclosedMessageTypesCase("", new string[0]),
            new EnclosedMessageTypesCase(";;", new string[0]),
        };
    }
}
