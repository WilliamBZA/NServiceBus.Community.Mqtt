// The golden wire payloads: the exact bytes of the MQTT payload (the JSON envelope) of sample messages, with the values they decode to.
// Both sides check every payload, whichever side produced it:
//
//   .NET origin    the .NET unit tests produce it with the .NET transport's WireFormat and NServiceBus's SystemJsonSerializer and check that the
//                  result matches byte for byte; the device unit tests decode it to the expected headers and body.
//   device origin  the device unit tests produce it from the same sample message and check that the result matches byte for byte; the .NET
//                  unit tests decode it with WireFormat and read the body with SystemJsonSerializer.
//
// Same subset as InteropContracts.cs: no generics, records, target-typed new, LINQ or nullable annotations.
// JSON text is kept in verbatim strings, so backslash sequences in it are the ones that are on the wire.

#nullable disable

using System;
using Contracts;

namespace Interop
{
    public class GoldenPayload
    {
        public readonly string Name;

        /// <summary>The payload as it is on the wire, as UTF-8 text.</summary>
        public readonly string Json;

        /// <summary>The message ID the envelope carries.</summary>
        public readonly string Id;

        /// <summary>The headers in the order the envelope writes them: key, value, key, value and so on.</summary>
        public readonly string[] Headers;

        /// <summary>The body, as UTF-8 JSON text, or an empty string for an empty body.</summary>
        public readonly string Body;

        /// <summary>The message the body was serialized from, or <c>null</c> for an empty body.</summary>
        public readonly object Message;

        public GoldenPayload(string name, string json, string id, string[] headers, string body, object message)
        {
            Name = name;
            Json = json;
            Id = id;
            Headers = headers;
            Body = body;
            Message = message;
        }
    }

    /// <summary>A payload that is not a valid message, which both sides must refuse to decode.</summary>
    public class InvalidPayload
    {
        public readonly string Name;
        public readonly string Json;

        public InvalidPayload(string name, string json)
        {
            Name = name;
            Json = json;
        }
    }

    /// <summary>A double and the text System.Text.Json writes for it. The device has to write the same text, and read it back to the same value.</summary>
    public class DoubleCase
    {
        public readonly double Value;
        public readonly string Json;

        public DoubleCase(double value, string json)
        {
            Value = value;
            Json = json;
        }
    }

    /// <summary>A time, as ticks, and the text System.Text.Json writes for a UTC time. The device has to write the same text, and read it back to the same ticks.</summary>
    public class DateTimeCase
    {
        public readonly long Ticks;
        public readonly string Json;

        public DateTimeCase(long ticks, string json)
        {
            Ticks = ticks;
            Json = json;
        }
    }

    /// <summary>A text a .NET endpoint can send for a time, and the ticks of the UTC time it means. A text without an offset means UTC. An invalid text has <c>-1</c> ticks.</summary>
    public class DateTimeParseCase
    {
        public readonly string Json;
        public readonly long UtcTicks;

        public DateTimeParseCase(string json, long utcTicks)
        {
            Json = json;
            UtcTicks = utcTicks;
        }
    }

    /// <summary>
    /// A time, as ticks, and the text of NServiceBus's wire time format (<c>yyyy-MM-dd HH:mm:ss:ffffff Z</c>, UTC) for it, as used by the
    /// <c>NServiceBus.TimeSent</c> and <c>NServiceBus.TimeOfFailure</c> headers. The format has microseconds, so ticks below that are cut off,
    /// and <see cref="ParsedTicks" /> is what a reader gets back.
    /// </summary>
    public class WireTimeCase
    {
        public readonly long Ticks;
        public readonly string Text;
        public readonly long ParsedTicks;

        public WireTimeCase(long ticks, string text)
        {
            Ticks = ticks;
            Text = text;
            ParsedTicks = ticks - ticks % 10L;
        }
    }

    /// <summary>A time span, as ticks, and the text of its constant (<c>c</c>) format, as used by the <c>NServiceBus.TimeToBeReceived</c> header.</summary>
    public class TimeSpanCase
    {
        public readonly long Ticks;
        public readonly string Text;

        public TimeSpanCase(long ticks, string text)
        {
            Ticks = ticks;
            Text = text;
        }
    }

    public static partial class WirePayloads
    {
        public const string MessageIdHeader = "NServiceBus.MessageId";
        public const string CommandId = "4c3d0f5e-2a76-4f4a-9a56-6a4e0f6b1c01";
        public const string EventId = "b2a1c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d";
        public const string SagaReplyId = "0f1e2d3c-4b5a-4968-8776-655443322110";
        public const string UnicodeHeadersId = "d4c3b2a1-f6e5-4d7c-9b8a-0123456789ab";
        public const string EmptyBodyId = "11223344-5566-4788-99aa-bbccddeeff00";
        public const string AllMemberTypesId = "aabbccdd-eeff-4011-8223-344556677889";

        public static readonly GoldenPayload Command = new GoldenPayload(
            "Command",
            @"{""Id"":""4c3d0f5e-2a76-4f4a-9a56-6a4e0f6b1c01"",""Headers"":{""NServiceBus.MessageId"":""4c3d0f5e-2a76-4f4a-9a56-6a4e0f6b1c01"",""NServiceBus.MessageIntent"":""Send"",""NServiceBus.ConversationId"":""7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10"",""NServiceBus.CorrelationId"":""4c3d0f5e-2a76-4f4a-9a56-6a4e0f6b1c01"",""NServiceBus.ReplyToAddress"":""Plant"",""NServiceBus.OriginatingMachine"":""PLANT-01"",""NServiceBus.OriginatingEndpoint"":""Plant"",""NServiceBus.ContentType"":""application/json"",""NServiceBus.EnclosedMessageTypes"":""Contracts.OpenValve, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"",""NServiceBus.Version"":""10.2.9"",""NServiceBus.TimeSent"":""2026-10-03 10:00:00:123456 Z""},""Body"":""eyJWYWx2ZUlkIjoiVi0xIiwiUGVyY2VudCI6NzV9""}",
            CommandId,
            new string[]
            {
                MessageIdHeader, CommandId,
                "NServiceBus.MessageIntent", "Send",
                "NServiceBus.ConversationId", "7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10",
                "NServiceBus.CorrelationId", CommandId,
                "NServiceBus.ReplyToAddress", "Plant",
                "NServiceBus.OriginatingMachine", "PLANT-01",
                "NServiceBus.OriginatingEndpoint", "Plant",
                "NServiceBus.ContentType", "application/json",
                "NServiceBus.EnclosedMessageTypes", "Contracts.OpenValve, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "NServiceBus.Version", "10.2.9",
                "NServiceBus.TimeSent", "2026-10-03 10:00:00:123456 Z",
            },
            @"{""ValveId"":""V-1"",""Percent"":75}",
            CreateOpenValve());

        public static readonly GoldenPayload Event = new GoldenPayload(
            "Event",
            @"{""Id"":""b2a1c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d"",""Headers"":{""NServiceBus.MessageId"":""b2a1c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d"",""NServiceBus.MessageIntent"":""Publish"",""NServiceBus.ConversationId"":""5d4c3b2a-1f0e-4d9c-8b7a-695847362514"",""NServiceBus.CorrelationId"":""b2a1c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d"",""NServiceBus.ReplyToAddress"":""Plant"",""NServiceBus.OriginatingEndpoint"":""Plant"",""NServiceBus.ContentType"":""application/json"",""NServiceBus.EnclosedMessageTypes"":""Contracts.ValveOpened, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.ValveEvent, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAlarm, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAudited, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"",""NServiceBus.Version"":""10.2.9"",""NServiceBus.TimeSent"":""2026-10-03 10:05:00:000001 Z""},""Body"":""eyJQZXJjZW50IjoxMDAsIlZhbHZlSWQiOiJWLTEifQ==""}",
            EventId,
            new string[]
            {
                MessageIdHeader, EventId,
                "NServiceBus.MessageIntent", "Publish",
                "NServiceBus.ConversationId", "5d4c3b2a-1f0e-4d9c-8b7a-695847362514",
                "NServiceBus.CorrelationId", EventId,
                "NServiceBus.ReplyToAddress", "Plant",
                "NServiceBus.OriginatingEndpoint", "Plant",
                "NServiceBus.ContentType", "application/json",
                // the type, its base class, then its interfaces in ordinal order
                "NServiceBus.EnclosedMessageTypes", "Contracts.ValveOpened, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.ValveEvent, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAlarm, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null;Contracts.IAudited, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "NServiceBus.Version", "10.2.9",
                "NServiceBus.TimeSent", "2026-10-03 10:05:00:000001 Z",
            },
            @"{""Percent"":100,""ValveId"":""V-1""}",
            CreateValveOpened());

        public static readonly GoldenPayload SagaReply = new GoldenPayload(
            "SagaReply",
            @"{""Id"":""0f1e2d3c-4b5a-4968-8776-655443322110"",""Headers"":{""NServiceBus.MessageId"":""0f1e2d3c-4b5a-4968-8776-655443322110"",""NServiceBus.MessageIntent"":""Reply"",""NServiceBus.RelatedTo"":""a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d"",""NServiceBus.ConversationId"":""7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10"",""NServiceBus.CorrelationId"":""a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d"",""NServiceBus.ReplyToAddress"":""Plant"",""NServiceBus.OriginatingEndpoint"":""Plant"",""NServiceBus.OriginatingSagaId"":""3f2e1d0c-9b8a-4796-8574-635241f0e1d2"",""NServiceBus.OriginatingSagaType"":""Plant.ValveSaga, Plant, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"",""NServiceBus.ContentType"":""application/json"",""NServiceBus.EnclosedMessageTypes"":""Contracts.ValveStatusResponse, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"",""NServiceBus.Version"":""10.2.9"",""NServiceBus.TimeSent"":""2026-10-03 10:10:00:500000 Z""},""Body"":""eyJWYWx2ZUlkIjoiVi0xIiwiSXNPcGVuIjp0cnVlLCJQZXJjZW50Ijo3NX0=""}",
            SagaReplyId,
            new string[]
            {
                MessageIdHeader, SagaReplyId,
                "NServiceBus.MessageIntent", "Reply",
                "NServiceBus.RelatedTo", "a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d",
                "NServiceBus.ConversationId", "7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10",
                "NServiceBus.CorrelationId", "a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d",
                "NServiceBus.ReplyToAddress", "Plant",
                "NServiceBus.OriginatingEndpoint", "Plant",
                "NServiceBus.OriginatingSagaId", "3f2e1d0c-9b8a-4796-8574-635241f0e1d2",
                "NServiceBus.OriginatingSagaType", "Plant.ValveSaga, Plant, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "NServiceBus.ContentType", "application/json",
                "NServiceBus.EnclosedMessageTypes", "Contracts.ValveStatusResponse, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "NServiceBus.Version", "10.2.9",
                "NServiceBus.TimeSent", "2026-10-03 10:10:00:500000 Z",
            },
            @"{""ValveId"":""V-1"",""IsOpen"":true,""Percent"":75}",
            CreateValveStatusResponse());

        // key and value characters that System.Text.Json writes as \uXXXX by default (non-ASCII, and ASCII such as + < > & ' ` and the quote),
        // the escapes it keeps short, and characters outside the basic plane as surrogate pairs
        public static readonly GoldenPayload UnicodeHeaders = new GoldenPayload(
            "UnicodeHeaders",
            @"{""Id"":""d4c3b2a1-f6e5-4d7c-9b8a-0123456789ab"",""Headers"":{""NServiceBus.MessageId"":""d4c3b2a1-f6e5-4d7c-9b8a-0123456789ab"",""NServiceBus.EnclosedMessageTypes"":""Contracts.OpenValve"",""a-\uD83D\uDE05-B7"":""a-\uD83D\uDE0D-b"",""plus\u002Bkey"":""v\u002B1 \u003Cx\u003E \u0026 \u0027q\u0027 \u0060t\u0060 \u0022quoted\u0022 back\\slash\nnew\ttab \u00E9\u20AC"",""\u65E5\u672C\u8A9E"":""\u00FCn\u00EF"",""slash/key"":""a/b""},""Body"":""eyJWYWx2ZUlkIjoiVi0xIiwiUGVyY2VudCI6NzV9""}",
            UnicodeHeadersId,
            new string[]
            {
                MessageIdHeader, UnicodeHeadersId,
                "NServiceBus.EnclosedMessageTypes", "Contracts.OpenValve",
                "a-😅-B7", "a-😍-b",
                "plus+key", "v+1 <x> & 'q' `t` \"quoted\" back\\slash\nnew\ttab é€",
                "日本語", "ünï",
                "slash/key", "a/b",
            },
            @"{""ValveId"":""V-1"",""Percent"":75}",
            CreateOpenValve());

        public static readonly GoldenPayload EmptyBody = new GoldenPayload(
            "EmptyBody",
            @"{""Id"":""11223344-5566-4788-99aa-bbccddeeff00"",""Headers"":{""NServiceBus.MessageId"":""11223344-5566-4788-99aa-bbccddeeff00"",""NServiceBus.MessageIntent"":""Send""},""Body"":""""}",
            EmptyBodyId,
            new string[]
            {
                MessageIdHeader, EmptyBodyId,
                "NServiceBus.MessageIntent", "Send",
            },
            "",
            null);

        public static readonly GoldenPayload AllMemberTypesPayload = new GoldenPayload(
            "AllMemberTypes",
            @"{""Id"":""aabbccdd-eeff-4011-8223-344556677889"",""Headers"":{""NServiceBus.MessageId"":""aabbccdd-eeff-4011-8223-344556677889"",""NServiceBus.MessageIntent"":""Send"",""NServiceBus.ContentType"":""application/json"",""NServiceBus.EnclosedMessageTypes"":""Contracts.AllMemberTypes, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null""},""Body"":""eyJUZXh0IjoiY2FmXHUwMEU5IFx1MDAyMnFcdTAwMjIgXHUwMDNDYlx1MDAzRVx1MDAyNlx1MDAyN1x1MDAyQlx1MDA2MCIsIkZsYWciOnRydWUsIkludDMyVmFsdWUiOi0yMTQ3NDgzNjQ4LCJJbnQ2NFZhbHVlIjoxMjM0NTY3ODkwMTIzNDU2Nzg5LCJEb3VibGVWYWx1ZSI6MC4xLCJUaW1lc3RhbXAiOiIyMDI2LTEwLTAzVDEwOjE1OjMwLjEyMzQ1NjdaIiwiTW9kZSI6MiwiTmVzdGVkIjp7Ik5hbWUiOiJuMSIsIkNvdW50Ijo3fSwiVGV4dHMiOlsiYSIsImJcdTIwQUMiXSwiTnVtYmVycyI6WzEsLTIsM10sIkRvdWJsZXMiOlsxLjUsLTAuMjUsMUUrMjFdLCJOZXN0ZWRJdGVtcyI6W3siTmFtZSI6IngiLCJDb3VudCI6MX0seyJOYW1lIjoieSIsIkNvdW50IjoyfV19""}",
            AllMemberTypesId,
            new string[]
            {
                MessageIdHeader, AllMemberTypesId,
                "NServiceBus.MessageIntent", "Send",
                "NServiceBus.ContentType", "application/json",
                "NServiceBus.EnclosedMessageTypes", "Contracts.AllMemberTypes, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            },
            @"{""Text"":""caf\u00E9 \u0022q\u0022 \u003Cb\u003E\u0026\u0027\u002B\u0060"",""Flag"":true,""Int32Value"":-2147483648,""Int64Value"":1234567890123456789,""DoubleValue"":0.1,""Timestamp"":""2026-10-03T10:15:30.1234567Z"",""Mode"":2,""Nested"":{""Name"":""n1"",""Count"":7},""Texts"":[""a"",""b\u20AC""],""Numbers"":[1,-2,3],""Doubles"":[1.5,-0.25,1E+21],""NestedItems"":[{""Name"":""x"",""Count"":1},{""Name"":""y"",""Count"":2}]}",
            CreateAllMemberTypes());

        /// <summary>The payloads the .NET transport produces.</summary>
        public static readonly GoldenPayload[] DotNetOrigin = new GoldenPayload[]
        {
            Command,
            Event,
            SagaReply,
            UnicodeHeaders,
            EmptyBody,
            AllMemberTypesPayload,
        };

        // ---- device origin: what the device writes. The device writes headers in the ordinal order of their UTF-8 bytes, and escapes only the quote,
        // the backslash and control characters, so everything else, including characters outside the basic plane, is raw UTF-8.

        public static readonly GoldenPayload DeviceUnicodeHeaders = new GoldenPayload(
            "DeviceUnicodeHeaders",
            @"{""Id"":""dev-1"",""Headers"":{""NServiceBus.MessageId"":""dev-1"",""a-😅-B7"":""a-😍-b"",""ctl"":""tab\there\nnl\r\b\f\u0001\u001F"",""q"":""\""quoted\"" back\\slash <&>+'"",""é"":""ü€""},""Body"":""e30=""}",
            "dev-1",
            new string[]
            {
                MessageIdHeader, "dev-1",
                "a-😅-B7", "a-😍-b",
                "ctl", "tab\there\nnl\r\b\f\u0001\u001f",
                "q", "\"quoted\" back\\slash <&>+'",
                "é", "ü€",
            },
            "{}",
            null);

        public static readonly GoldenPayload DeviceEmptyBody = new GoldenPayload(
            "DeviceEmptyBody",
            @"{""Id"":""dev-2"",""Headers"":{""NServiceBus.MessageId"":""dev-2"",""NServiceBus.MessageIntent"":""Send""},""Body"":""""}",
            "dev-2",
            new string[]
            {
                MessageIdHeader, "dev-2",
                "NServiceBus.MessageIntent", "Send",
            },
            "",
            null);

        // The messages the endpoint itself writes. They are produced by an endpoint named Device_01 whose clock is fixed at 2026-10-03 10:15:30.123456 UTC
        // and whose message IDs run id-1, id-2 and so on, and the bodies are in the ordinal order of the member names.

        public const string DeviceTimeText = "2026-10-03 10:15:30:123456 Z";

        /// <summary>The stack trace text the failed message golden carries. The runtime's own text is replaced by this one before the comparison.</summary>
        public const string DeviceFailedStackTrace = "   at Sample.OpenValveHandler.Handle(Object message, IMessageHandlerContext context)";

        // OpenValve sent to Plant from outside a handler
        public static readonly GoldenPayload DeviceCommand = new GoldenPayload(
            "DeviceCommand",
            @"{""Id"":""id-1"",""Headers"":{""NServiceBus.ContentType"":""application/json"",""NServiceBus.ConversationId"":""id-2"",""NServiceBus.CorrelationId"":""id-1"",""NServiceBus.EnclosedMessageTypes"":""Contracts.OpenValve"",""NServiceBus.MessageId"":""id-1"",""NServiceBus.MessageIntent"":""Send"",""NServiceBus.OriginatingEndpoint"":""Device_01"",""NServiceBus.ReplyToAddress"":""Device_01"",""NServiceBus.TimeSent"":""2026-10-03 10:15:30:123456 Z""},""Body"":""eyJQZXJjZW50Ijo3NSwiVmFsdmVJZCI6IlYtMSJ9""}",
            "id-1",
            new string[]
            {
                "NServiceBus.ContentType", "application/json",
                "NServiceBus.ConversationId", "id-2",
                "NServiceBus.CorrelationId", "id-1",
                "NServiceBus.EnclosedMessageTypes", "Contracts.OpenValve",
                MessageIdHeader, "id-1",
                "NServiceBus.MessageIntent", "Send",
                "NServiceBus.OriginatingEndpoint", "Device_01",
                "NServiceBus.ReplyToAddress", "Device_01",
                "NServiceBus.TimeSent", DeviceTimeText,
            },
            @"{""Percent"":75,""ValveId"":""V-1""}",
            CreateOpenValve());

        // the reply to a request from a saga, in the conversation of the request (message a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d)
        public static readonly GoldenPayload DeviceReply = new GoldenPayload(
            "DeviceReply",
            @"{""Id"":""id-1"",""Headers"":{""NServiceBus.ContentType"":""application/json"",""NServiceBus.ConversationId"":""7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10"",""NServiceBus.CorrelationId"":""a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d"",""NServiceBus.EnclosedMessageTypes"":""Contracts.ValveStatusResponse"",""NServiceBus.MessageId"":""id-1"",""NServiceBus.MessageIntent"":""Reply"",""NServiceBus.OriginatingEndpoint"":""Device_01"",""NServiceBus.RelatedTo"":""a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d"",""NServiceBus.ReplyToAddress"":""Device_01"",""NServiceBus.SagaId"":""3f2e1d0c-9b8a-4796-8574-635241f0e1d2"",""NServiceBus.SagaType"":""Plant.ValveSaga, Plant, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"",""NServiceBus.TimeSent"":""2026-10-03 10:15:30:123456 Z""},""Body"":""eyJJc09wZW4iOnRydWUsIlBlcmNlbnQiOjc1LCJWYWx2ZUlkIjoiVi0xIn0=""}",
            "id-1",
            new string[]
            {
                "NServiceBus.ContentType", "application/json",
                "NServiceBus.ConversationId", "7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10",
                "NServiceBus.CorrelationId", "a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d",
                "NServiceBus.EnclosedMessageTypes", "Contracts.ValveStatusResponse",
                MessageIdHeader, "id-1",
                "NServiceBus.MessageIntent", "Reply",
                "NServiceBus.OriginatingEndpoint", "Device_01",
                "NServiceBus.RelatedTo", "a7b6c5d4-e3f2-4a1b-9c8d-7e6f5a4b3c2d",
                "NServiceBus.ReplyToAddress", "Device_01",
                "NServiceBus.SagaId", "3f2e1d0c-9b8a-4796-8574-635241f0e1d2",
                "NServiceBus.SagaType", "Plant.ValveSaga, Plant, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "NServiceBus.TimeSent", DeviceTimeText,
            },
            @"{""IsOpen"":true,""Percent"":75,""ValveId"":""V-1""}",
            CreateValveStatusResponse());

        // ValveOpened published from outside a handler: every copy is this payload, and the types are listed in the order of the hierarchy
        public static readonly GoldenPayload DeviceEvent = new GoldenPayload(
            "DeviceEvent",
            @"{""Id"":""id-1"",""Headers"":{""NServiceBus.ContentType"":""application/json"",""NServiceBus.ConversationId"":""id-2"",""NServiceBus.CorrelationId"":""id-1"",""NServiceBus.EnclosedMessageTypes"":""Contracts.ValveOpened;Contracts.ValveEvent;Contracts.IAlarm;Contracts.IAudited"",""NServiceBus.MessageId"":""id-1"",""NServiceBus.MessageIntent"":""Publish"",""NServiceBus.OriginatingEndpoint"":""Device_01"",""NServiceBus.ReplyToAddress"":""Device_01"",""NServiceBus.TimeSent"":""2026-10-03 10:15:30:123456 Z""},""Body"":""eyJQZXJjZW50IjoxMDAsIlZhbHZlSWQiOiJWLTEifQ==""}",
            "id-1",
            new string[]
            {
                "NServiceBus.ContentType", "application/json",
                "NServiceBus.ConversationId", "id-2",
                "NServiceBus.CorrelationId", "id-1",
                "NServiceBus.EnclosedMessageTypes", "Contracts.ValveOpened;Contracts.ValveEvent;Contracts.IAlarm;Contracts.IAudited",
                MessageIdHeader, "id-1",
                "NServiceBus.MessageIntent", "Publish",
                "NServiceBus.OriginatingEndpoint", "Device_01",
                "NServiceBus.ReplyToAddress", "Device_01",
                "NServiceBus.TimeSent", DeviceTimeText,
            },
            @"{""Percent"":100,""ValveId"":""V-1""}",
            CreateValveOpened());

        // the .NET-origin Command golden, after its handler threw on every attempt: the original headers and body, plus the failure headers
        public static readonly GoldenPayload DeviceFailedMessage = new GoldenPayload(
            "DeviceFailedMessage",
            @"{""Id"":""4c3d0f5e-2a76-4f4a-9a56-6a4e0f6b1c01"",""Headers"":{""NServiceBus.ContentType"":""application/json"",""NServiceBus.ConversationId"":""7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10"",""NServiceBus.CorrelationId"":""4c3d0f5e-2a76-4f4a-9a56-6a4e0f6b1c01"",""NServiceBus.EnclosedMessageTypes"":""Contracts.OpenValve, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"",""NServiceBus.ExceptionInfo.ExceptionType"":""System.InvalidOperationException"",""NServiceBus.ExceptionInfo.Message"":""The valve is stuck."",""NServiceBus.ExceptionInfo.StackTrace"":""   at Sample.OpenValveHandler.Handle(Object message, IMessageHandlerContext context)"",""NServiceBus.FailedQ"":""Device_01"",""NServiceBus.MessageId"":""4c3d0f5e-2a76-4f4a-9a56-6a4e0f6b1c01"",""NServiceBus.MessageIntent"":""Send"",""NServiceBus.OriginatingEndpoint"":""Plant"",""NServiceBus.OriginatingMachine"":""PLANT-01"",""NServiceBus.ProcessingEndpoint"":""Device_01"",""NServiceBus.ReplyToAddress"":""Plant"",""NServiceBus.TimeOfFailure"":""2026-10-03 10:15:30:123456 Z"",""NServiceBus.TimeSent"":""2026-10-03 10:00:00:123456 Z"",""NServiceBus.Version"":""10.2.9""},""Body"":""eyJWYWx2ZUlkIjoiVi0xIiwiUGVyY2VudCI6NzV9""}",
            CommandId,
            new string[]
            {
                "NServiceBus.ContentType", "application/json",
                "NServiceBus.ConversationId", "7b1c2d0e-9f33-4d1e-8a6b-5c0d4e3f2a10",
                "NServiceBus.CorrelationId", CommandId,
                "NServiceBus.EnclosedMessageTypes", "Contracts.OpenValve, Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                "NServiceBus.ExceptionInfo.ExceptionType", "System.InvalidOperationException",
                "NServiceBus.ExceptionInfo.Message", "The valve is stuck.",
                "NServiceBus.ExceptionInfo.StackTrace", DeviceFailedStackTrace,
                "NServiceBus.FailedQ", "Device_01",
                MessageIdHeader, CommandId,
                "NServiceBus.MessageIntent", "Send",
                "NServiceBus.OriginatingEndpoint", "Plant",
                "NServiceBus.OriginatingMachine", "PLANT-01",
                "NServiceBus.ProcessingEndpoint", "Device_01",
                "NServiceBus.ReplyToAddress", "Plant",
                "NServiceBus.TimeOfFailure", DeviceTimeText,
                "NServiceBus.TimeSent", "2026-10-03 10:00:00:123456 Z",
                "NServiceBus.Version", "10.2.9",
            },
            @"{""ValveId"":""V-1"",""Percent"":75}",
            CreateOpenValve());

        /// <summary>The payloads the device produces.</summary>
        public static readonly GoldenPayload[] DeviceOrigin = new GoldenPayload[]
        {
            DeviceUnicodeHeaders,
            DeviceEmptyBody,
            DeviceCommand,
            DeviceReply,
            DeviceEvent,
            DeviceFailedMessage,
        };

        public static OpenValve CreateOpenValve()
        {
            var message = new OpenValve();
            message.ValveId = "V-1";
            message.Percent = 75;
            return message;
        }

        public static ValveOpened CreateValveOpened()
        {
            var message = new ValveOpened();
            message.ValveId = "V-1";
            message.Percent = 100;
            return message;
        }

        public static ValveStatusResponse CreateValveStatusResponse()
        {
            var message = new ValveStatusResponse();
            message.ValveId = "V-1";
            message.IsOpen = true;
            message.Percent = 75;
            return message;
        }

        /// <summary>One value of every supported member type, chosen to be hard: escaped and non-ASCII text, the extremes of int and long, a double that has no short binary form and one in exponent form, and a timestamp with sub-millisecond ticks.</summary>
        public static AllMemberTypes CreateAllMemberTypes()
        {
            var message = new AllMemberTypes();
            message.Text = "café \"q\" <b>&'+`";
            message.Flag = true;
            message.Int32Value = -2147483648;
            message.Int64Value = 1234567890123456789L;
            message.DoubleValue = 0.1;
            message.Timestamp = new DateTime(639266193301234567L, DateTimeKind.Utc);
            message.Mode = Mode.Faulted;
            message.Nested = CreateNested("n1", 7);
            message.Texts = new string[] { "a", "b€" };
            message.Numbers = new int[] { 1, -2, 3 };
            message.Doubles = new double[] { 1.5, -0.25, 1e21 };
            message.NestedItems = new NestedMember[] { CreateNested("x", 1), CreateNested("y", 2) };
            return message;
        }

        public static ArrayMemberTypes CreateArrayMemberTypes()
        {
            var message = new ArrayMemberTypes();
            message.Flags = new bool[] { true, false, true };
            message.Longs = new long[] { long.MinValue, 0L, long.MaxValue };
            message.Timestamps = new DateTime[] { new DateTime(639266193301234567L, DateTimeKind.Utc), new DateTime(504911232000000000L, DateTimeKind.Utc) };
            message.Modes = new Mode[] { Mode.Faulted, Mode.Idle, Mode.Running };
            message.Texts = new string[] { "a", null, "c" };
            message.Empty = new int[0];
            return message;
        }

        /// <summary>Compares two <see cref="ArrayMemberTypes" /> member by member. Returns <c>null</c> when they are equal, or a description of the first difference.</summary>
        public static string Difference(ArrayMemberTypes expected, ArrayMemberTypes actual)
        {
            if (actual == null) return "the message is null";
            if (actual.Flags == null || expected.Flags.Length != actual.Flags.Length) return "Flags: expected " + expected.Flags.Length + " items";
            for (var i = 0; i < expected.Flags.Length; i++)
            {
                if (expected.Flags[i] != actual.Flags[i]) return "Flags[" + i + "]: expected " + expected.Flags[i] + " but was " + actual.Flags[i];
            }

            if (actual.Longs == null || expected.Longs.Length != actual.Longs.Length) return "Longs: expected " + expected.Longs.Length + " items";
            for (var i = 0; i < expected.Longs.Length; i++)
            {
                if (expected.Longs[i] != actual.Longs[i]) return "Longs[" + i + "]: expected " + expected.Longs[i] + " but was " + actual.Longs[i];
            }

            if (actual.Timestamps == null || expected.Timestamps.Length != actual.Timestamps.Length) return "Timestamps: expected " + expected.Timestamps.Length + " items";
            for (var i = 0; i < expected.Timestamps.Length; i++)
            {
                if (expected.Timestamps[i].Ticks != actual.Timestamps[i].Ticks) return "Timestamps[" + i + "]: expected ticks " + expected.Timestamps[i].Ticks + " but was " + actual.Timestamps[i].Ticks;
            }

            if (actual.Modes == null || expected.Modes.Length != actual.Modes.Length) return "Modes: expected " + expected.Modes.Length + " items";
            for (var i = 0; i < expected.Modes.Length; i++)
            {
                if (expected.Modes[i] != actual.Modes[i]) return "Modes[" + i + "]: expected " + (int)expected.Modes[i] + " but was " + (int)actual.Modes[i];
            }

            if (actual.Texts == null || expected.Texts.Length != actual.Texts.Length) return "Texts: expected " + expected.Texts.Length + " items";
            for (var i = 0; i < expected.Texts.Length; i++)
            {
                if (!SameText(expected.Texts[i], actual.Texts[i])) return "Texts[" + i + "]: expected '" + expected.Texts[i] + "' but was '" + actual.Texts[i] + "'";
            }

            if (actual.Empty == null || actual.Empty.Length != 0) return "Empty: expected an empty array";

            return null;
        }

        // On nanoFramework two null strings are not equal with ==, so strings are compared by this.
        static bool SameText(string expected, string actual)
        {
            if (expected == null || actual == null)
            {
                return expected == null && actual == null;
            }

            return expected == actual;
        }

        static NestedMember CreateNested(string name, int count)
        {
            var nested = new NestedMember();
            nested.Name = name;
            nested.Count = count;
            return nested;
        }

        /// <summary>Compares two <see cref="AllMemberTypes" /> member by member. Returns <c>null</c> when they are equal, or a description of the first difference.</summary>
        public static string Difference(AllMemberTypes expected, AllMemberTypes actual)
        {
            if (actual == null)
            {
                return "the message is null";
            }

            if (!SameText(expected.Text, actual.Text)) return "Text: expected '" + expected.Text + "' but was '" + actual.Text + "'";
            if (expected.Flag != actual.Flag) return "Flag: expected " + expected.Flag + " but was " + actual.Flag;
            if (expected.Int32Value != actual.Int32Value) return "Int32Value: expected " + expected.Int32Value + " but was " + actual.Int32Value;
            if (expected.Int64Value != actual.Int64Value) return "Int64Value: expected " + expected.Int64Value + " but was " + actual.Int64Value;
            if (expected.DoubleValue != actual.DoubleValue) return "DoubleValue: expected " + expected.DoubleValue + " but was " + actual.DoubleValue;
            if (expected.Timestamp.Ticks != actual.Timestamp.Ticks) return "Timestamp: expected ticks " + expected.Timestamp.Ticks + " but was " + actual.Timestamp.Ticks;
            if (expected.Mode != actual.Mode) return "Mode: expected " + (int)expected.Mode + " but was " + (int)actual.Mode;

            var nested = Difference("Nested", expected.Nested, actual.Nested);
            if (nested != null) return nested;

            if (expected.Texts.Length != actual.Texts.Length) return "Texts: expected " + expected.Texts.Length + " items but was " + (actual.Texts == null ? "null" : actual.Texts.Length.ToString());
            for (var i = 0; i < expected.Texts.Length; i++)
            {
                if (!SameText(expected.Texts[i], actual.Texts[i])) return "Texts[" + i + "]: expected '" + expected.Texts[i] + "' but was '" + actual.Texts[i] + "'";
            }

            if (actual.Numbers == null || expected.Numbers.Length != actual.Numbers.Length) return "Numbers: expected " + expected.Numbers.Length + " items";
            for (var i = 0; i < expected.Numbers.Length; i++)
            {
                if (expected.Numbers[i] != actual.Numbers[i]) return "Numbers[" + i + "]: expected " + expected.Numbers[i] + " but was " + actual.Numbers[i];
            }

            if (actual.Doubles == null || expected.Doubles.Length != actual.Doubles.Length) return "Doubles: expected " + expected.Doubles.Length + " items";
            for (var i = 0; i < expected.Doubles.Length; i++)
            {
                if (expected.Doubles[i] != actual.Doubles[i]) return "Doubles[" + i + "]: expected " + expected.Doubles[i] + " but was " + actual.Doubles[i];
            }

            if (actual.NestedItems == null || expected.NestedItems.Length != actual.NestedItems.Length) return "NestedItems: expected " + expected.NestedItems.Length + " items";
            for (var i = 0; i < expected.NestedItems.Length; i++)
            {
                var item = Difference("NestedItems[" + i + "]", expected.NestedItems[i], actual.NestedItems[i]);
                if (item != null) return item;
            }

            return null;
        }

        static string Difference(string name, NestedMember expected, NestedMember actual)
        {
            if (actual == null) return name + ": the member is null";
            if (!SameText(expected.Name, actual.Name)) return name + ".Name: expected '" + expected.Name + "' but was '" + actual.Name + "'";
            if (expected.Count != actual.Count) return name + ".Count: expected " + expected.Count + " but was " + actual.Count;
            return null;
        }
    }
}
