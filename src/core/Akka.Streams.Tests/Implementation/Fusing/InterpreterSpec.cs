using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Supervision;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using OnNext = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup<int>.OnNext;
using OnComplete = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup<int>.OnComplete;
using RequestOne = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.OneBoundedSetup<int>.RequestOne;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class InterpreterSpec : GraphInterpreterSpecKit
    {
        /*
         * These tests were writtern for the previous version of the interpreter, the so called OneBoundedInterpreter.
         * These stages are now properly emulated by the GraphInterpreter and many of the edge cases were relevant to
         * the execution model of the old one. Still, these tests are very valuable, so please do not remove.
         */

        public InterpreterSpec(ITestOutputHelper output = null) : base(output)
        {
        }

        [Fact]
        public void Interpreter_should_implement_map_correctly()
        {
            WithOneBoundedSetup<int>(ToGraphStage(new Map<int, int>(x => x + 1, Deciders.StoppingDecider)),
                (lastEvents, upstream, downstream) =>
                {
                    lastEvents().Should().BeEmpty();

                    downstream.RequestOne();
                    lastEvents().Should().Equal(new RequestOne());

                    upstream.OnNext(0);
                    lastEvents().Should().Equal(new OnNext(1));

                    downstream.RequestOne();
                    lastEvents().Should().Equal(new RequestOne());

                    upstream.OnNext(1);
                    lastEvents().Should().Equal(new OnNext(2));

                    upstream.OnComplete();
                    lastEvents().Should().Equal(new OnComplete());
                });
        }
    }
}