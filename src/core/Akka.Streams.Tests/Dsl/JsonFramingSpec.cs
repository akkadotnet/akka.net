//-----------------------------------------------------------------------
// <copyright file="JsonFramingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
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
                    list.Add(s.DecodeString());
                    return list;
                }, Materializer);

            result.AwaitResult().ShouldAllBeEquivalentTo(new []
            {
                @"{ ""name"" : ""john"" }",
                @"{ ""name"" : ""Ég get etið gler án þess að meiða mig"" }",
                @"{ ""name"" : ""jack"" }"
            });
        }
    }
}
