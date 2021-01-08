//-----------------------------------------------------------------------
// <copyright file="JsonFramingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Streams.Util;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class JsonFramingSpec : AkkaSpec
    {
        public JsonFramingSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = Sys.Materializer();
        }

        private ActorMaterializer Materializer { get; }

        [Fact]
        public void Collecting_multiple_json_should_parse_json_array()
        {
            var input = @"
           [
            { ""name"" : ""john"" },
            { ""name"" : ""Ég get etið gler án þess að meiða mig"" },
            { ""name"" : ""jack"" },
           ]";

            var result = Source.Single(ByteString.FromString(input))
                .Via(JsonFraming.ObjectScanner(int.MaxValue))
                .RunAggregate(new List<string>(), (list, s) =>
                {
                    list.Add(s.ToString());
                    return list;
                }, Materializer);

            result.AwaitResult().ShouldAllBeEquivalentTo(new []
            {
                @"{ ""name"" : ""john"" }",
                @"{ ""name"" : ""Ég get etið gler án þess að meiða mig"" }",
                @"{ ""name"" : ""jack"" }"
            });
        }

        [Fact]
        public void Collecting_multiple_json_should_emit_single_json_element_from_string()
        {
            var input = @"
            { ""name"" : ""john"" }
            { ""name"" : ""jack"" }
           ";

            var result = Source.Single(ByteString.FromString(input))
                .Via(JsonFraming.ObjectScanner(int.MaxValue))
                .Take(1)
                .RunAggregate(new List<string>(), (list, s) =>
                {
                    list.Add(s.ToString());
                    return list;
                }, Materializer);

            result.AwaitResult().Should().HaveCount(1).And.Subject.Should().Contain(@"{ ""name"" : ""john"" }");
        }

        [Fact]
        public void Collecting_multiple_json_should_parse_line_delimited()
        {
            var input = @"
            { ""name"" : ""john"" }
            { ""name"" : ""jack"" }
            { ""name"" : ""katie"" }
           ";

            var result = Source.Single(ByteString.FromString(input))
                .Via(JsonFraming.ObjectScanner(int.MaxValue))
                .RunAggregate(new List<string>(), (list, s) =>
                {
                    list.Add(s.ToString());
                    return list;
                }, Materializer);


            result.AwaitResult().ShouldAllBeEquivalentTo(new[]
            {
                @"{ ""name"" : ""john"" }",
                @"{ ""name"" : ""jack"" }",
                @"{ ""name"" : ""katie"" }"
            });
        }

        [Fact]
        public void Collecting_multiple_json_should_parse_comma_delimited()
        {
            var input = @"{ ""name"" : ""john"" }, { ""name"" : ""jack"" }, { ""name"" : ""katie"" }
           ";

            var result = Source.Single(ByteString.FromString(input))
                .Via(JsonFraming.ObjectScanner(int.MaxValue))
                .RunAggregate(new List<string>(), (list, s) =>
                {
                    list.Add(s.ToString());
                    return list;
                }, Materializer);


            result.AwaitResult().ShouldAllBeEquivalentTo(new[]
            {
                @"{ ""name"" : ""john"" }",
                @"{ ""name"" : ""jack"" }",
                @"{ ""name"" : ""katie"" }"
            });

        }

        [Fact]
        public void Collecting_multiple_json_should_parse_chunks_successfully()
        {
            var input = new[]
            {
                ByteString.FromString(@"[{ ""name"" : ""john"" "), ByteString.FromString("},"),
                ByteString.FromString("{ \"na"), ByteString.FromString("me\" : \"jack\" "),
                ByteString.FromString("}]")
            };
            var result = Source.From(input)
                .Via(JsonFraming.ObjectScanner(int.MaxValue))
                .RunAggregate(new List<string>(), (list, s) =>
                {
                    list.Add(s.ToString());
                    return list;
                }, Materializer)
                .AwaitResult();


            result.ShouldAllBeEquivalentTo(new[]
            {
                @"{ ""name"" : ""john"" }",
                @"{ ""name"" : ""jack"" }"
            });
        }

        [Fact]
        public void Collecting_multiple_json_should_emit_all_elements_after_input_completes()
        {
            var input = this.CreatePublisherProbe<ByteString>();
            var output = this.CreateSubscriberProbe<string>();

            var result =
                Source.FromPublisher(input)
                    .Via(JsonFraming.ObjectScanner(int.MaxValue))
                    .Select(b => b.ToString())
                    .RunWith(Sink.FromSubscriber(output), Materializer);

            output.Request(1);
            input.ExpectRequest();
            input.SendNext(ByteString.FromString("[{\"a\":0}, {\"b\":1}, {\"c\":2}, {\"d\":3}, {\"e\":4}]"));
            input.SendComplete();
            Thread.Sleep(100); // another of those races, we don't know the order of next and complete
            output.ExpectNext("{\"a\":0}");
            output.Request(1);
            output.ExpectNext("{\"b\":1}");
            output.Request(1);
            output.ExpectNext("{\"c\":2}");
            output.Request(1);
            output.ExpectNext("{\"d\":3}");
            output.Request(1);
            output.ExpectNext("{\"e\":4}");
            output.Request(1);
            output.ExpectComplete();
        }

        [Fact]
        public void Collecting_json_buffer_when_nothing_is_supplied_should_return_nothing()
            => new JsonObjectParser().Poll().Should().Be(Option<ByteString>.None);

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_empty_object()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{}"));
            buffer.Poll().Value.ToString().Should().Be("{}");
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_string_value()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{ \"name\": \"john\" }"));
            buffer.Poll().Value.ToString().Should().Be("{ \"name\": \"john\" }");
        }

        [Fact] 
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_string_value_containing_space()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{ \"name\": \"john doe\" }"));
            buffer.Poll().Value.ToString().Should().Be("{ \"name\": \"john doe\" }");
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_string_value_containing_single_quote()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{ \"name\": \"john o'doe\" }"));
            buffer.Poll().Value.ToString().Should().Be("{ \"name\": \"john o'doe\" }");
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_string_value_containing_curly_brace()
        {

            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{ \"name\": \"john{"));
            buffer.Offer(ByteString.FromString("}"));
            buffer.Offer(ByteString.FromString("\""));
            buffer.Offer(ByteString.FromString("}"));
            buffer.Poll().Value.ToString().Should().Be("{ \"name\": \"john{}\"}");
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_string_value_containing_curly_brace_and_escape_character()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{ \"name\": \"john"));
            buffer.Offer(ByteString.FromString("\\\""));
            buffer.Offer(ByteString.FromString("{"));
            buffer.Offer(ByteString.FromString("}"));
            buffer.Offer(ByteString.FromString("\\\""));
            buffer.Offer(ByteString.FromString(" "));
            buffer.Offer(ByteString.FromString("hey"));
            buffer.Offer(ByteString.FromString("\""));
            buffer.Offer(ByteString.FromString("}"));
            buffer.Poll().Value.ToString().Should().Be("{ \"name\": \"john\\\"{}\\\" hey\"}");
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_integer_value()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{ \"age\" : 101}"));
            buffer.Poll().Value.ToString().Should().Be("{ \"age\" : 101}");
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_decimal_value()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{ \"age\" : 10.1}"));
            buffer.Poll().Value.ToString().Should().Be("{ \"age\" : 10.1}");
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_nested_object()
        {
            var buffer = new JsonObjectParser();
            const string content = "{ \"name\" : \"john\"," +
                                   "  \"age\"  : 101," +
                                   "  \"address\": {" +
                                   "       \"street\": \"Straight Street\"," +
                                   "       \"postcode\": 1234" +
                                   "  }" +
                                   "}";
            buffer.Offer(ByteString.FromString(content));
            buffer.Poll().Value.ToString().Should().Be(content);
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_single_field_having_multiple_level_of_nested_object()
        {
            var buffer = new JsonObjectParser();
            const string content = "{ \"name\" : \"john\"," +
                                   "  \"age\"  : 101," +
                                   "  \"address\": {" +
                                   "       \"street\": {" +
                                   "            \"name\": \"Straight\"," +
                                   "            \"type\": \"Avenue\"" +
                                   "       }," +
                                   "       \"postcode\": 1234" +
                                   "  }" +
                                   "}";
            buffer.Offer(ByteString.FromString(content));
            buffer.Poll().Value.ToString().Should().Be(content);
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_an_escaped_backslash_followed_by_a_double_quote()
        {
            var buffer = new JsonObjectParser();
            const string content = @"{ ""key"": ""\\"" }";

            buffer.Offer(ByteString.FromString(content));
            buffer.Poll().Value.ToString().Should().Be(content);
        }


        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_one_object_should_successfully_parse_a_string_that_contains_an_escaped_quote()
        {
            var buffer = new JsonObjectParser();
            const string content = "{ \"key\": \"\\\"\" }";

            buffer.Offer(ByteString.FromString(content));
            buffer.Poll().Value.ToString().Should().Be(content);
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_nested_array_should_successfully_parse()
        {
            var buffer = new JsonObjectParser();
            const string content = "{ \"name\" : \"john\"," +
                                   "  \"things\": [" +
                                   "      1," +
                                   "      \"hey\"," +
                                   "      3" +
                                   "      \"there\"" +
                                   "  ]" +
                                   "}";
            buffer.Offer(ByteString.FromString(content));
            buffer.Poll().Value.ToString().Should().Be(content);
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_complex_object_graph_should_successfully_parse()
        {
            var buffer = new JsonObjectParser();
            const string content = @"{
                 ""name"": ""john"",
                 ""addresses"": [
                   {
                     ""street"": ""3 Hopson Street"",
                     ""postcode"": ""ABC-123"",
                     ""tags"": [""work"", ""office""],
                     ""contactTime"": [
                       {""time"": ""0900-1800"", ""timezone"", ""UTC""}
                     ]
                   },
                   {
                     ""street"": ""12 Adielie Road"",
                     ""postcode"": ""ZZY-888"",
                     ""tags"": [""home""],
                     ""contactTime"": [
                       {""time"": ""0800-0830"", ""timezone"", ""UTC""},
                       {""time"": ""1800-2000"", ""timezone"", ""UTC""}
                     ]
                   }
                 ]
               }";
            buffer.Offer(ByteString.FromString(content));
            buffer.Poll().Value.ToString().Should().Be(content);
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_multiple_fields_should_parse_successfully()
        {
            var buffer = new JsonObjectParser();
            const string content = "{ \"name\": \"john\", \"age\" : 101}";
            buffer.Offer(ByteString.FromString(content));
            buffer.Poll().Value.ToString().Should().Be(content);
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_multiple_fields_should_parse_successfully_despite_valid_whitespaces_around_json()
        {
            var buffer = new JsonObjectParser();
            const string content = "        " +
                                   "        " +
                                   "{ \"name\": \"john\"" +
                                   ", \"age\" : 101}";
            buffer.Offer(ByteString.FromString(content));
            buffer.Poll().Value.ToString().Should().Be(content.TrimStart());
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_has_multiple_objects_should_pops_the_right_object_as_buffer_is_filled()
        {
            var buffer = new JsonObjectParser();
            const string input1 = @"{
                 ""name"": ""john"",
                 ""age"": 32
               }";
            const string input2 = @"{
                 ""name"": ""katie"",
                 ""age"": 25
               }";
            buffer.Offer(ByteString.FromString(input1 + "," + input2));
            buffer.Poll().Value.ToString().Should().Be(input1);
            buffer.Poll().Value.ToString().Should().Be(input2);

            buffer.Poll().Should().Be(Option<ByteString>.None);
            buffer.Offer(ByteString.FromString("{\"name\":\"jenkins\",\"age\": "));
            buffer.Poll().Should().Be(Option<ByteString>.None);

            buffer.Offer(ByteString.FromString("65 }"));
            buffer.Poll().Value.ToString().Should().Be("{\"name\":\"jenkins\",\"age\": 65 }");
        }

        [Fact]
        public void Collecting_json_buffer_when_valid_json_is_supplied_which_returns_none_until_valid_json_is_encountered()
        {
            var buffer = new JsonObjectParser();
            @"{ ""name"" : ""john""".ForEach(c =>
            {
                buffer.Offer(ByteString.FromString(c.ToString()));
                buffer.Poll().Should().Be(Option<ByteString>.None);
            });

            buffer.Offer(ByteString.FromString("}"));
            buffer.Poll().Value.ShouldAllBeEquivalentTo(ByteString.FromString(@"{ ""name"" : ""john""}"));
        }

        [Fact]
        public void Collecting_json_buffer_when_invalid_json_is_supplied_should_fail_if_it_is_broken_from_the_start()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("THIS IS NOT VALID { \name\": \"john\"}"));
            buffer.Invoking(b => b.Poll()).ShouldThrow<Framing.FramingException>();
        }

        [Fact]
        public void Collecting_json_buffer_when_invalid_json_is_supplied_should_fail_if_it_is_broken_at_the_end()
        {
            var buffer = new JsonObjectParser();
            buffer.Offer(ByteString.FromString("{ \"name\": \"john\"} THIS IS NOT VALID "));
            buffer.Poll(); // first emitting the valid element
            buffer.Invoking(b => b.Poll()).ShouldThrow<Framing.FramingException>();
        }

        [Fact]
        public void Collecting_multiple_json_should_fail_on_too_large_initial_object()
        {
            var input = @"{ ""name"": ""john"" },{ ""name"": ""jack"" }";

            var result = Source.Single(ByteString.FromString(input))
                .Via(JsonFraming.ObjectScanner(5))
                .Select(b => b.ToString())
                .RunAggregate(new List<string>(), (list, s) =>
                {
                    list.Add(s);
                    return list;
                }, Materializer);

            result.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Framing.FramingException>();
        }

        [Fact]
        public void Collecting_multiple_json_should_fail_when_2nd_object_is_too_large()
        {
            var input = @"
                 { ""name"": ""john"" },
                 { ""name"": ""jack"" },
                 { ""name"": ""very very long name somehow. how did this happen?"" }"
                .Split(',').Select(ByteString.FromString);

            var probe = Source.From(input)
                .Via(JsonFraming.ObjectScanner(48))
                .RunWith(this.SinkProbe<ByteString>(), Materializer);

            probe.EnsureSubscription();

            probe.Request(1).ExpectNext(ByteString.FromString(@"{ ""name"": ""john"" }"));
            probe.Request(1).ExpectNext(ByteString.FromString(@"{ ""name"": ""jack"" }"));
            probe.Request(1).ExpectError().Message.Should().Contain("exceeded");
        }
    }
}
