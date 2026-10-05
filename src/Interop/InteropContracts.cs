// The sample message contracts, shared as source by the device unit tests, the sample device app, the .NET unit tests and the interop host.
//
// This file is compiled by two compilers: the .NET SDK's C# compiler and the nanoFramework one. It therefore sticks to the common subset:
// no generics, records, target-typed new, LINQ or nullable annotations, and no member initializers. NServiceBus identifies a type on the wire
// only by its namespace-qualified name, so a device and a .NET endpoint must use the same namespace and class names, which is what sharing
// this source guarantees.

#nullable disable

using System;
using NServiceBus;

namespace Contracts
{
    /// <summary>A command the .NET side sends to the device.</summary>
    public class OpenValve : ICommand
    {
        public string ValveId { get; set; }
        public int Percent { get; set; }
    }

    /// <summary>The base of the valve events. A device that only knows this type still receives <see cref="ValveOpened" />.</summary>
    public class ValveEvent : IEvent
    {
        public string ValveId { get; set; }
    }

    /// <summary>Implements two interfaces, so it also serves the "two subscribers to two different interfaces" case.</summary>
    public interface IAlarm : IEvent
    {
    }

    public interface IAudited : IEvent
    {
    }

    /// <summary>The hierarchy is: <c>Contracts.ValveOpened</c>, <c>Contracts.ValveEvent</c>, <c>Contracts.IAlarm</c>, <c>Contracts.IAudited</c>.</summary>
    public class ValveOpened : ValveEvent, IAlarm, IAudited
    {
        public int Percent { get; set; }
    }

    /// <summary>An event that a device subscribes to at runtime.</summary>
    public class PriceChanged : IEvent
    {
        public string Sku { get; set; }
        public double Price { get; set; }
    }

    /// <summary>A request that the receiver answers with <see cref="ValveStatusResponse" />.</summary>
    public class ValveStatusRequest : ICommand
    {
        public string ValveId { get; set; }
    }

    public class ValveStatusResponse : IMessage
    {
        public string ValveId { get; set; }
        public bool IsOpen { get; set; }
        public int Percent { get; set; }
    }

    /// <summary>What the sample device sends the interop host when it has received a <see cref="ValveEvent" />, to show that the subscription to the base type works.</summary>
    public class ValveEventAcknowledged : ICommand
    {
        public string ValveId { get; set; }
    }

    /// <summary>A command whose handler always throws, to exercise retries and the error queue.</summary>
    public class AlwaysFails : ICommand
    {
        public string Reason { get; set; }
    }

    public enum Mode
    {
        Idle = 0,
        Running = 1,
        Faulted = 2
    }

    public class NestedMember
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }

    /// <summary>Arrays of the member types that <see cref="AllMemberTypes" /> has no array of, an empty array, and a string array with a <c>null</c> element.</summary>
    public class ArrayMemberTypes : ICommand
    {
        public bool[] Flags { get; set; }
        public long[] Longs { get; set; }
        public DateTime[] Timestamps { get; set; }
        public Mode[] Modes { get; set; }
        public string[] Texts { get; set; }
        public int[] Empty { get; set; }
    }

    /// <summary>One property of each supported member type: string, bool, int, long, double, DateTime in UTC, enum, nested class, and arrays of these.</summary>
    public class AllMemberTypes : ICommand
    {
        public string Text { get; set; }
        public bool Flag { get; set; }
        public int Int32Value { get; set; }
        public long Int64Value { get; set; }
        public double DoubleValue { get; set; }
        public DateTime Timestamp { get; set; }
        public Mode Mode { get; set; }
        public NestedMember Nested { get; set; }
        public string[] Texts { get; set; }
        public int[] Numbers { get; set; }
        public double[] Doubles { get; set; }
        public NestedMember[] NestedItems { get; set; }
    }
}
