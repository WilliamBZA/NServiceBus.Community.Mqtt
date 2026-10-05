// The payload and conversion cases only the codec tests need, kept apart from the golden messages in WirePayloads.cs: the device tests that
// build whole messages link just WirePayloads.cs, because an assembly on nanoFramework can hold only so many strings.
//
// Same subset as InteropContracts.cs: no generics, records, target-typed new, LINQ or nullable annotations.

#nullable disable

using System;
using Contracts;

namespace Interop
{
    public static partial class WirePayloads
    {
        /// <summary>
        /// Hand-written payloads that a reader must accept although the .NET transport never writes them in this form: any property order,
        /// whitespace, unknown properties, every JSON escape, and null or missing values where they are allowed. Both sides decode every one.
        /// A payload with no <c>Id</c> has <c>null</c> as its expected ID.
        /// </summary>
        public static readonly GoldenPayload[] DecodeCases = new GoldenPayload[]
        {
            new GoldenPayload(
                "PropertyOrder",
                @"{""Body"":""e30="",""Headers"":{""NServiceBus.MessageId"":""m1""},""Id"":""m1""}",
                "m1",
                new string[] { MessageIdHeader, "m1" },
                "{}",
                null),

            new GoldenPayload(
                "Whitespace",
                "  {\r\n\t\"Id\" :\t\"m2\" ,\n \"Headers\"\r: {\n \"NServiceBus.MessageId\" : \"m2\" , \"k\" : \"v\" } ,\n\"Body\": \"e30=\"\r\n}  \n",
                "m2",
                new string[] { MessageIdHeader, "m2", "k", "v" },
                "{}",
                null),

            new GoldenPayload(
                "UnknownProperties",
                @"{""Extra"":{""a"":[1,2.5e10,-0.0,true,false,null,""s"",{},[],{""n"":{""m"":[[]]}}],""b"":""x\""y""},""Id"":""m3"",""Headers"":{""NServiceBus.MessageId"":""m3""},""Tail"":[],""Body"":""e30="",""N"":null,""Num"":-12.5E-3}",
                "m3",
                new string[] { MessageIdHeader, "m3" },
                "{}",
                null),

            new GoldenPayload(
                "Escapes",
                @"{""Id"":""m4"",""Headers"":{""NServiceBus.MessageId"":""m4"",""key"":""\""\\\/\b\f\n\r\tAé€😀😍"",""sl\/ash"":""a/b""},""Body"":""e30=""}",
                "m4",
                new string[] { MessageIdHeader, "m4", "key", "\"\\/\b\f\n\r\tAé€😀😍", "sl/ash", "a/b" },
                "{}",
                null),

            new GoldenPayload(
                "RawUtf8",
                "{\"Id\":\"m5\",\"Headers\":{\"NServiceBus.MessageId\":\"m5\",\"café\":\"日本語 😅\"},\"Body\":\"e30=\"}",
                "m5",
                new string[] { MessageIdHeader, "m5", "café", "日本語 😅" },
                "{}",
                null),

            new GoldenPayload(
                "NullId",
                @"{""Id"":null,""Headers"":{""NServiceBus.MessageId"":""m6""},""Body"":""""}",
                null,
                new string[] { MessageIdHeader, "m6" },
                "",
                null),

            new GoldenPayload(
                "NoId",
                @"{""Headers"":{""NServiceBus.MessageId"":""m7""},""Body"":""YQ==""}",
                null,
                new string[] { MessageIdHeader, "m7" },
                "a",
                null),

            new GoldenPayload(
                "Base64Padding",
                @"{""Id"":""m8"",""Headers"":{""NServiceBus.MessageId"":""m8""},""Body"":""YWJj""}",
                "m8",
                new string[] { MessageIdHeader, "m8" },
                "abc",
                null),

            new GoldenPayload(
                "Base64OnePad",
                @"{""Id"":""m9"",""Headers"":{""NServiceBus.MessageId"":""m9""},""Body"":""YWI=""}",
                "m9",
                new string[] { MessageIdHeader, "m9" },
                "ab",
                null),

            new GoldenPayload(
                "DuplicateKeys",
                @"{""Id"":""x"",""Id"":""m10"",""Headers"":{""k"":""first"",""k"":""second"",""NServiceBus.MessageId"":""m10""},""Body"":""YQ==""}",
                "m10",
                new string[] { MessageIdHeader, "m10", "k", "second" },
                "a",
                null),

            new GoldenPayload(
                "NullHeaderValue",
                @"{""Id"":""m11"",""Headers"":{""NServiceBus.MessageId"":""m11"",""empty"":null},""Body"":""e30=""}",
                "m11",
                new string[] { MessageIdHeader, "m11", "empty", null },
                "{}",
                null),

            new GoldenPayload(
                "EmptyKeyAndValue",
                @"{""Id"":""m12"",""Headers"":{""NServiceBus.MessageId"":""m12"","""":""""},""Body"":""e30=""}",
                "m12",
                new string[] { MessageIdHeader, "m12", "", "" },
                "{}",
                null),
        };

        /// <summary>Payloads that are not valid messages. Both sides must refuse them.</summary>
        public static readonly InvalidPayload[] InvalidPayloads = new InvalidPayload[]
        {
            new InvalidPayload("Garbage", "not json"),
            new InvalidPayload("JsonNull", "null"),
            new InvalidPayload("JsonNullWithWhitespace", "  null \n"),
            new InvalidPayload("JsonArray", "[]"),
            new InvalidPayload("JsonString", "\"text\""),
            new InvalidPayload("JsonNumber", "123"),
            new InvalidPayload("Empty", ""),
            new InvalidPayload("WhitespaceOnly", "  \r\n "),
            new InvalidPayload("EmptyObject", "{}"),
            new InvalidPayload("HeadersNull", @"{""Id"":""x"",""Headers"":null,""Body"":""e30=""}"),
            new InvalidPayload("HeadersMissing", @"{""Id"":""x"",""Body"":""e30=""}"),
            new InvalidPayload("BodyNull", @"{""Id"":""x"",""Headers"":{},""Body"":null}"),
            new InvalidPayload("BodyMissing", @"{""Id"":""x"",""Headers"":{}}"),
            new InvalidPayload("HeaderValueIsANumber", @"{""Id"":""x"",""Headers"":{""k"":1},""Body"":""e30=""}"),
            new InvalidPayload("HeaderValueIsAnObject", @"{""Id"":""x"",""Headers"":{""k"":{}},""Body"":""e30=""}"),
            new InvalidPayload("HeadersIsAnArray", @"{""Id"":""x"",""Headers"":[],""Body"":""e30=""}"),
            new InvalidPayload("IdIsANumber", @"{""Id"":1,""Headers"":{},""Body"":""e30=""}"),
            new InvalidPayload("BodyIsNotBase64", @"{""Id"":""x"",""Headers"":{},""Body"":""%%%%""}"),
            new InvalidPayload("BodyHasABadLength", @"{""Id"":""x"",""Headers"":{},""Body"":""abc""}"),
            new InvalidPayload("BodyIsANumber", @"{""Id"":""x"",""Headers"":{},""Body"":5}"),
            new InvalidPayload("Truncated", @"{""Id"":""x"",""Headers"":{""k"":""v"""),
            new InvalidPayload("DataAfterTheEnd", @"{""Id"":""x"",""Headers"":{},""Body"":""""} x"),
            new InvalidPayload("UnterminatedString", @"{""Id"":""x"),
            new InvalidPayload("InvalidEscape", @"{""Id"":""\x"",""Headers"":{},""Body"":""""}"),
            new InvalidPayload("ShortUnicodeEscape", @"{""Id"":""\u12"",""Headers"":{},""Body"":""""}"),
            new InvalidPayload("LoneHighSurrogate", @"{""Id"":""\uD83D"",""Headers"":{},""Body"":""""}"),
            new InvalidPayload("LoneLowSurrogate", @"{""Id"":""\uDE00"",""Headers"":{},""Body"":""""}"),
            new InvalidPayload("HighSurrogateThenANonSurrogate", @"{""Id"":""\uD83DA"",""Headers"":{},""Body"":""""}"),
            new InvalidPayload("ControlCharacterInAString", "{\"Id\":\"a\tb\",\"Headers\":{},\"Body\":\"\"}"),
            new InvalidPayload("UnquotedPropertyName", @"{Id:""x"",""Headers"":{},""Body"":""""}"),
            new InvalidPayload("SingleQuotes", @"{'Id':'x','Headers':{},'Body':''}"),
            new InvalidPayload("TrailingComma", @"{""Id"":""x"",""Headers"":{},""Body"":"""",}"),
            new InvalidPayload("MissingColon", @"{""Id"" ""x"",""Headers"":{},""Body"":""""}"),
            new InvalidPayload("MissingComma", @"{""Id"":""x"" ""Headers"":{},""Body"":""""}"),
            new InvalidPayload("InvalidLiteral", @"{""Id"":tru,""Headers"":{},""Body"":""""}"),
            new InvalidPayload("LeadingZeroInAnUnknownProperty", @"{""X"":01,""Headers"":{},""Body"":""""}"),
            new InvalidPayload("UnfinishedNumberInAnUnknownProperty", @"{""X"":1.,""Headers"":{},""Body"":""""}"),
            new InvalidPayload("UnclosedArrayInAnUnknownProperty", @"{""X"":[1,2,""Headers"":{},""Body"":""""}"),
            new InvalidPayload("MismatchedContainerInAnUnknownProperty", @"{""X"":[1,2},""Headers"":{},""Body"":""""}"),
            new InvalidPayload("TooDeeplyNestedUnknownProperty", DeepArray(70)),
        };

        static string DeepArray(int depth)
        {
            var open = "";
            var close = "";
            for (var i = 0; i < depth; i++)
            {
                open += "[";
                close += "]";
            }

            return "{\"X\":" + open + close + ",\"Headers\":{},\"Body\":\"\"}";
        }

        // ---- message bodies of the sample contracts. The .NET bodies are what NServiceBus's System.Text.Json serializer writes, in the order of the
        // members' declaration. The device bodies are what the device writes: members in the ordinal order of their names, no member that is null,
        // and everything outside the quote, the backslash and control characters as raw UTF-8.

        public const string ArrayMemberTypesDotNetBody =
            @"{""Flags"":[true,false,true],""Longs"":[-9223372036854775808,0,9223372036854775807],""Timestamps"":[""2026-10-03T10:15:30.1234567Z"",""1601-01-01T00:00:00Z""],""Modes"":[2,0,1],""Texts"":[""a"",null,""c""],""Empty"":[]}";

        public const string ArrayMemberTypesDeviceBody =
            @"{""Empty"":[],""Flags"":[true,false,true],""Longs"":[-9223372036854775808,0,9223372036854775807],""Modes"":[2,0,1],""Texts"":[""a"",null,""c""],""Timestamps"":[""2026-10-03T10:15:30.1234567Z"",""1601-01-01T00:00:00Z""]}";

        public const string AllMemberTypesDeviceBody =
            @"{""DoubleValue"":0.1,""Doubles"":[1.5,-0.25,1E+21],""Flag"":true,""Int32Value"":-2147483648,""Int64Value"":1234567890123456789,""Mode"":2,""Nested"":{""Count"":7,""Name"":""n1""},""NestedItems"":[{""Count"":1,""Name"":""x""},{""Count"":2,""Name"":""y""}],""Numbers"":[1,-2,3],""Text"":""café \""q\"" <b>&'+`"",""Texts"":[""a"",""b€""],""Timestamp"":""2026-10-03T10:15:30.1234567Z""}";

        public static readonly DoubleCase[] Doubles = new DoubleCase[]
        {
            new DoubleCase(0.1, "0.1"),
            new DoubleCase(0.30000000000000004, "0.30000000000000004"),
            new DoubleCase(1.5, "1.5"),
            new DoubleCase(-0.25, "-0.25"),
            new DoubleCase(100, "100"),
            new DoubleCase(0, "0"),
            new DoubleCase(4.35, "4.35"),
            new DoubleCase(12345.6789, "12345.6789"),
            new DoubleCase(123456789.12345679, "123456789.12345679"),
            new DoubleCase(3.141592653589793, "3.141592653589793"),
            new DoubleCase(1.0 / 3.0, "0.3333333333333333"),
            new DoubleCase(2.0 / 3.0, "0.6666666666666666"),
            new DoubleCase(9007199254740993.0, "9007199254740992"),
            new DoubleCase(0.000123, "0.000123"),
            new DoubleCase(0.0001, "0.0001"),
            new DoubleCase(100000000000000.0, "100000000000000"),

            // the plain form ends at 1E+16, and from 1E-05 down the form with an exponent starts
            new DoubleCase(1e15, "1000000000000000"),
            new DoubleCase(1e16, "10000000000000000"),
            new DoubleCase(12345678901234567.0, "12345678901234568"),
            new DoubleCase(1e17, "1E+17"),
            new DoubleCase(1.5e17, "1.5E+17"),
            new DoubleCase(1e21, "1E+21"),
            new DoubleCase(1234567890123456789.0, "1.2345678901234568E+18"),
            new DoubleCase(1.5000000000000002E-05, "1.5000000000000002E-05"),
            new DoubleCase(0.00001, "1E-05"),
            new DoubleCase(1e-7, "1E-07"),
            new DoubleCase(-1e-7, "-1E-07"),
            new DoubleCase(2.5e-300, "2.5E-300"),
            new DoubleCase(double.MaxValue, "1.7976931348623157E+308"),
            new DoubleCase(double.MinValue, "-1.7976931348623157E+308"),
            new DoubleCase(5E-324, "5E-324"),

            // subnormals, powers of two and neighbours of them, random bit patterns and values of the kind a calculation gives, written by bits,
            // with the text System.Text.Json writes for each
            new DoubleCase(BitConverter.Int64BitsToDouble(0x0000000000000001L), "5E-324"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x0000000000000002L), "1E-323"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x0000000000000003L), "1.5E-323"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x000FFFFFFFFFFFFFL), "2.225073858507201E-308"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x0010000000000000L), "2.2250738585072014E-308"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x0010000000000001L), "2.225073858507202E-308"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FF0000000000000L), "1"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FF0000000000001L), "1.0000000000000002"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FEFFFFFFFFFFFFFL), "0.9999999999999999"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4000000000000000L), "2"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FE0000000000000L), "0.5"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4340000000000000L), "9007199254740992"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4340000000000001L), "9007199254740994"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x7FEFFFFFFFFFFFFEL), "1.7976931348623155E+308"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x7FE0000000000000L), "8.98846567431158E+307"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4330000000000000L), "4503599627370496"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x7FD5555555555555L), "5.992310449541053E+307"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4631501856787818086L), "-0.1"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4377498837804122111L), "-9007199254740994"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4314EFEAE8007491L), "1473322931985700.2"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3A8A44B4B3C13298L), "1.0609715116496602E-26"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4619030095531166805L), "-0.6846436504762229"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4350A6E015D861CDL), "18748323987031860"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3CA3885C22378321L), "1.3553502659133672E-16"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3B969C3DE6020EF1L), "1.1969803475492189E-21"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4656E4C004A9E9D9L), "7.255257173337057E+30"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x48D2A1E2B86C1B83L), "6.492431883793219E+42"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3B9F77440965A546L), "1.6657899181430832E-21"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x475F9DBCF0224D89L), "6.5664204053911054E+35"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3ED52B85D1570DA1L), "5.047323857923682E-06"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4327125525167780340L), "-2.186164782136038E+19"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x44F3EB8AF5A1F6C2L), "1.5051193779083635E+24"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-5070767528487788372L), "-4.194481784097808E-31"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3D9992E2D34835ECL), "5.81480289377419E-12"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3BB9509634C4208EL), "5.360615711485876E-21"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x482DCBFDEDB0B633L), "5.0696701979441916E+39"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3CE9B09EB1CE13A5L), "2.8521542335319094E-15"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x40405181EB55685CL), "32.63677732153221"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3E094FBFD8C7F80AL), "7.366622186927444E-10"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x43B3EB3ECA184B8FL), "1.4353099439595558E+18"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4834409635513200451L), "-2.745221715206725E-15"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-3861335706675305237L), "-3.0141795165906944E+50"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3DF14CACD44FB7B4L), "2.5174103453848715E-10"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x42FD85B2508B5DF9L), "519360951662047.56"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4930619998644299289L), "-1.0015574774701788E-21"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4820478718124547467L), "-2.3287433983135497E-14"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x47B2184A50BDB2B4L), "2.40522263440057E+37"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3D71742365103588L), "9.92125994881706E-13"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4811A52E4F9C5BBBL), "1.5010908293285747E+39"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x399DF16A7DCBA83DL), "3.690763543973576E-31"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x409C443D299B1F79L), "1809.0597290265998"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3F375224950C0064L), "0.0003558482467696021"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3808ACCFBBA5BB81L), "9.064179910814011E-39"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3DCA8721E60C6EA3L), "4.825389806137892E-11"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x382B654D67DC6575L), "4.0254381405717075E-38"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4150336592787206930L), "-1.4604865715946337E+31"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4AEAFDBE66B7DB0DL), "8.07888144900621E+52"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4834646275641FD8L), "6.939081364063992E+39"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x41D4B549BE906BC2L), "1389700858.256577"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4502E3FE621BCEBBL), "2.8546668108926E+24"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x47F6200C66CCA480L), "4.705507350442063E+38"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4212431713514132372L), "-1.0227021275545861E+27"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x45D5FA0C4DF30B3FL), "2.720589906984861E+28"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4400800484463154148L), "-256985960392544.88"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x480BA2D1A8460223L), "1.1755059306521718E+39"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x41FD07AC3E8C1BABL), "7792673768.756755"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4016931635430386192L), "-1.156417773551269E+40"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x38ADF00360A5EF62L), "1.1261255266960272E-35"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x387EDC910F7B1F43L), "1.4511049150811037E-36"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x45D2D28DE3A93357L), "2.330109860872183E+28"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x38BBE4906EB199F4L), "2.0984232164788338E-35"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4A950A259E8721DCL), "1.9679656466530463E+51"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FA9CB0F41C77850L), "0.050377346782383925"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4062280207848009855L), "-1.0594458366711389E+37"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x468FBFA20306435BL), "8.0492177122671435E+31"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x460A3AAC340AB6F3L), "2.59761317117774E+29"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x40577BB745245677L), "93.93306091831788"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x47C842BDDD2DB928L), "6.44960287809819E+37"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3C0049C75B86E340L), "1.1037312424463413E-19"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3942416965FF8B13L), "7.031768406080835E-33"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3E038E121CFBA7BEL), "5.691243535177267E-10"),
            new DoubleCase(BitConverter.Int64BitsToDouble(-4895532855146618772L), "-2.139500904412648E-19"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x399F804666CAB60EL), "3.882807178447133E-31"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3D7DD7B06A7EFB0DL), "1.6963517535107449E-12"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x432E3295FE965DFDL), "4249934552051454.5"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4A6197985F54EB39L), "2.056878676708278E+50"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3E3FD2E0A6622408L), "7.409541957539321E-09"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3E60FD38A96D2461L), "3.164475008991378E-08"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3C758EA2B723262BL), "1.8697864731921214E-17"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x40355EB851EB851FL), "21.37"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FB1EB851EB851ECL), "0.07"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x408FAA0000000000L), "1013.25"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x40424CCCCCCCCCCDL), "36.6"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4058A66666666666L), "98.6"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FDE2BE2BE2BE2BEL), "0.4714285714285714"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FC2492492492492L), "0.14285714285714285"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4009249249249249L), "3.142857142857143"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4040AAAAAAAAAAABL), "33.333333333333336"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FD3333333333334L), "0.30000000000000004"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FE9999999999999L), "0.7999999999999999"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x404DFC28F5C28F5CL), "59.97"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FA1EB851EB851ECL), "0.035"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x4005666666666666L), "2.675"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FF0147AE147AE14L), "1.005"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x446696695DBD1CC3L), "3.3333333333333335E+21"),
            new DoubleCase(BitConverter.Int64BitsToDouble(0x3FDC71C71C71C71CL), "0.4444444444444444"),
        };

        public static readonly DateTimeCase[] DateTimes = new DateTimeCase[]
        {
            new DateTimeCase(639266193301234567L, "2026-10-03T10:15:30.1234567Z"),
            new DateTimeCase(639266193300000000L, "2026-10-03T10:15:30Z"),
            new DateTimeCase(639266193301230000L, "2026-10-03T10:15:30.123Z"),
            new DateTimeCase(639266193301000000L, "2026-10-03T10:15:30.1Z"),
            new DateTimeCase(639266193300100000L, "2026-10-03T10:15:30.01Z"),
            new DateTimeCase(639266193300000001L, "2026-10-03T10:15:30.0000001Z"),
            new DateTimeCase(504911232000000000L, "1601-01-01T00:00:00Z"),
            new DateTimeCase(630822816000000000L, "2000-01-01T00:00:00Z"),
            new DateTimeCase(638448479999999999L, "2024-02-29T23:59:59.9999999Z"),
        };

        public static readonly DateTimeParseCase[] DateTimeParses = new DateTimeParseCase[]
        {
            // fractions of any length, the digits after the seventh dropped
            new DateTimeParseCase("2026-10-03T10:15:30.1Z", 639266193301000000L),
            new DateTimeParseCase("2026-10-03T10:15:30.01Z", 639266193300100000L),
            new DateTimeParseCase("2026-10-03T10:15:30.12345678Z", 639266193301234567L),
            new DateTimeParseCase("2026-10-03T10:15:30.Z", 639266193300000000L),

            // offsets are applied
            new DateTimeParseCase("2026-10-03T12:15:30+02:00", 639266193300000000L),
            new DateTimeParseCase("2026-10-03T10:15:30-05:30", 639266391300000000L),

            // no offset, no seconds, no time at all
            new DateTimeParseCase("2026-10-03T10:15:30", 639266193300000000L),
            new DateTimeParseCase("2026-10-03T10:15", 639266193000000000L),
            new DateTimeParseCase("2026-10-03", 639265824000000000L),

            // invalid
            new DateTimeParseCase("2026-13-01T00:00:00Z", -1L),
            new DateTimeParseCase("2026-02-30T00:00:00Z", -1L),
            new DateTimeParseCase("2025-02-29T00:00:00Z", -1L),
            new DateTimeParseCase("2026-10-03T24:00:00Z", -1L),
            new DateTimeParseCase("2026-10-03T10:60:00Z", -1L),
            new DateTimeParseCase("2026-10-03T10:15:60Z", -1L),
            new DateTimeParseCase("2026-10-03 10:15:30Z", -1L),
            new DateTimeParseCase("2026-10-03T10:15:30z", -1L),
            new DateTimeParseCase("2026-10-03T10:15:30+0200", -1L),
            new DateTimeParseCase("2026-10-03T10:15:30Z ", -1L),
            new DateTimeParseCase("26-10-03T10:15:30Z", -1L),
            new DateTimeParseCase("", -1L),
            new DateTimeParseCase("x", -1L),
        };

        public static readonly WireTimeCase[] WireTimes = new WireTimeCase[]
        {
            new WireTimeCase(639266193301234567L, "2026-10-03 10:15:30:123456 Z"),
            new WireTimeCase(639266193300000000L, "2026-10-03 10:15:30:000000 Z"),
            new WireTimeCase(639266193300000001L, "2026-10-03 10:15:30:000000 Z"),
            new WireTimeCase(639266193300000010L, "2026-10-03 10:15:30:000001 Z"),
            new WireTimeCase(639266193309999999L, "2026-10-03 10:15:30:999999 Z"),
            new WireTimeCase(639266193300100000L, "2026-10-03 10:15:30:010000 Z"),
            new WireTimeCase(504911232000000000L, "1601-01-01 00:00:00:000000 Z"),
            new WireTimeCase(630822816000000000L, "2000-01-01 00:00:00:000000 Z"),
            new WireTimeCase(638448479999999999L, "2024-02-29 23:59:59:999999 Z"),
        };

        public static readonly TimeSpanCase[] TimeSpans = new TimeSpanCase[]
        {
            new TimeSpanCase(0L, "00:00:00"),
            new TimeSpanCase(300000000L, "00:00:30"),
            new TimeSpanCase(15000000L, "00:00:01.5000000"),
            new TimeSpanCase(1L, "00:00:00.0000001"),
            new TimeSpanCase(35390000000L, "00:58:59"),
            new TimeSpanCase(36000000000L, "01:00:00"),
            new TimeSpanCase(937840000000L, "1.02:03:04"),
            new TimeSpanCase(937845000000L, "1.02:03:04.5000000"),
            new TimeSpanCase(6048000000000L, "7.00:00:00"),
            new TimeSpanCase(-900000000L, "-00:01:30"),
            new TimeSpanCase(-1L, "-00:00:00.0000001"),
            new TimeSpanCase(long.MaxValue, "10675199.02:48:05.4775807"),
            new TimeSpanCase(long.MinValue, "-10675199.02:48:05.4775808"),
        };

    }
}
