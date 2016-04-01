using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSupervisionSpec : AkkaSpec
    {
        private static readonly SystemException Exception = new SystemException("simulated exception");
        private static Flow<int, int, Unit> FailingMap => Flow.Create<int>().Map(n =>
        {
            if (n == 3)
                throw Exception;
            return n;
        });
        
        private ActorMaterializer Materializer { get; }

        public FlowSupervisionSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        private IImmutableList<int> Run(IGraph<FlowShape<int, int>, Unit> flow)
        {
            var task =
                Source.From(Enumerable.Range(1, 5).Concat(Enumerable.Range(1, 5).ToList()))
                    .Via(flow)
                    .Limit(1000)
                    .RunWith(Sink.Seq<int>(), Materializer);
            
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            return task.Result;
        }

        [Fact]
        public void Stream_superviosion_must_stop_and_complete_stream_with_failure_by_default()
        {
            Action action = () => Run(FailingMap);
            action.ShouldThrow<SystemException>().And.ShouldBeEquivalentTo(Exception);
        }

        [Fact]
        public void Stream_superviosion_must_support_resume()
        {
            var withAttributes =
                FailingMap.WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider));
            var result = Run(withAttributes);
            result.ShouldAllBeEquivalentTo(new [] {1,2,4,5,1,2,4,5});
        }

        [Fact]
        public void Stream_superviosion_must_support_restart()
        {
            var withAttributes =
                FailingMap.WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider));
            var result = Run(withAttributes);
            result.ShouldAllBeEquivalentTo(new[] { 1, 2, 4, 5, 1, 2, 4, 5 });
        }

        [Fact]
        public void Stream_superviosion_must_complete_stream_with_ArgumentNullException_when_null_is_emitted()
        {
            var task = Source.From(new[] {"a", "b"}).Map(x => null as string).Limit(1000).RunWith(Sink.Seq<string>(), Materializer);
            
            task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3)))
                .ShouldThrow<AggregateException>()
                .WithInnerException<ArgumentNullException>()
                .WithInnerMessage(ReactiveStreamsCompliance.ElementMustNotBeNullMsg + "Parametername: element");
        }

        [Fact]
        public void Stream_superviosion_must_resume_stream_when_null_is_emitted()
        {
            var nullMap = Flow.Create<string>().Map(element =>
            {
                if (element == "b")
                    return null;
                return element;
            }).WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider));
            var task = Source.From(new[] {"a", "b", "c"})
                .Via(nullMap)
                .Limit(1000)
                .RunWith(Sink.Seq<string>(), Materializer);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new [] {"a", "c"});
        }
    }
}
