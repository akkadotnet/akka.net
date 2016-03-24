using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
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
            var input = Enumerable.Range(1, 6);
            var future = Source.From(input).RunWith(Sink.Seq<int>(), Materializer);
            future.Wait(300).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);
        }

        [Fact]
        public void Sink_ToSeq_must_return_an_empty_SeqT_from_an_empty_Source()
        {
            var input = Enumerable.Empty<int>();
            var future = Source.FromEnumerator(() => input.GetEnumerator()).RunWith(Sink.Seq<int>(), Materializer);
            future.Wait(300).Should().BeTrue();
            future.Result.ShouldAllBeEquivalentTo(input);
        }
    }
}
