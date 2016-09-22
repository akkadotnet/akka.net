//-----------------------------------------------------------------------
// <copyright file="SeqSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class SeqSinkSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SeqSinkSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void Sink_ToSeq_must_return_a_SeqT_from_a_Source()
        {
            var input = Enumerable.Range(1, 6).ToList();
            var future = Source.From(input).RunWith(Sink.Seq<int>(), Materializer);
            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);
        }

        [Fact]
        public void Sink_ToSeq_must_return_an_empty_SeqT_from_an_empty_Source()
        {
            var input = Enumerable.Empty<int>();
            var future = Source.FromEnumerator(() => input.GetEnumerator()).RunWith(Sink.Seq<int>(), Materializer);
            future.Wait(RemainingOrDefault).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);
        }
    }
}
