//-----------------------------------------------------------------------
// <copyright file="FlowSectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSectionSpec : AkkaSpec
    {
        private const string Config = @"
                my-dispatcher1 {
                  type = Dispatcher
                  executor = ""fork-join-executor""
                  fork-join-executor {
                    parallelism-min = 8
                    parallelism-max = 8
                  }
                  mailbox-requirement = ""Akka.Dispatch.IUnboundedMessageQueueSemantics""
                }
                my-dispatcher1 {
                  type = Dispatcher
                  executor = ""fork-join-executor""
                  fork-join-executor {
                    parallelism-min = 8
                    parallelism-max = 8
                  }
                  mailbox-requirement = ""Akka.Dispatch.IUnboundedMessageQueueSemantics""
                }";

        private ActorMaterializer Materializer { get; }

        public FlowSectionSpec(ITestOutputHelper helper) : base(Config, helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact(Skip = "Thread name must be set from the dispatcher")]
        public void A_Flow_can_have_an_op_with_a_different_dispatcher()
        {
            var flow = Flow.Create<int>()
                .Select(x => SentThreadNameTo(TestActor, x))
                .WithAttributes(ActorAttributes.CreateDispatcher("my-dispatcher1"));

            Source.Single(1).Via(flow).To(Sink.Ignore<int>()).Run(Materializer);

            ExpectMsg<string>().Should().Contain("my-dispatcher1");
        }

        [Fact(Skip = "Thread name must be set from the dispatcher")]
        public void A_Flow_can_have_a_nested_flow_with_a_different_dispatcher()
        {
            Source.Single(1)
                .Via(
                    Flow.Create<int>()
                        .Select(x => SentThreadNameTo(TestActor, x))
                        .WithAttributes(ActorAttributes.CreateDispatcher("my-dispatcher")))
                .To(Sink.Ignore<int>())
                .Run(Materializer);

            ExpectMsg<string>().Should().Contain("my-dispatcher1");
        }

        [Fact(Skip = "Thread name must be set from the dispatcher")]
        public void A_Flow_can_have_multiple_levels_of_nesting()
        {
            var probe1 = CreateTestProbe();
            var probe2 = CreateTestProbe();

            var flow1 =
                Flow.Create<int>()
                    .Select(x => SentThreadNameTo(probe1.Ref, x))
                    .WithAttributes(ActorAttributes.CreateDispatcher("my-dispatcher1"));

            var flow2 = flow1
                    .Via(Flow.Create<int>().Select(x => SentThreadNameTo(probe2.Ref, x)))
                    .WithAttributes(ActorAttributes.CreateDispatcher("my-dispatcher2"));

            Source.Single(1).Via(flow2).To(Sink.Ignore<int>()).Run(Materializer);

            probe1.ExpectMsg<string>().Should().Contain("my-dispatcher1");
            probe2.ExpectMsg<string>().Should().Contain("my-dispatcher2");
        }

        [Fact(Skip = "FIXME: Flow has no simple toString anymore")]
        public void A_Flow_can_include_name_in_ToString()
        {
            var n = "Uppercase reverser";
            var f1 = Flow.Create<string>().Select(c => c.ToLower());
            var f2 =
                Flow.Create<string>()
                    .Select(c => c.ToUpper())
                    .Select(s => s.Reverse().Aggregate("", (agg, c) => agg + c))
                    .Named(n)
                    .Select(c => c.ToLower());

            f1.Via(f2).ToString().Should().Contain(n);
        }

        [Fact(Skip = "Thread name must be set from the dispatcher")]
        public void A_Flow_can_have_an_op_section_with_different_dispatcher_and_name()
        {
            var defaultDispatcher = CreateTestProbe();
            var customDispatcher = CreateTestProbe();

            var f1 = Flow.Create<int>().Select(x => SentThreadNameTo(defaultDispatcher.Ref, x));
            var f2 =
                Flow.Create<int>()
                    .Select(x => SentThreadNameTo(defaultDispatcher.Ref, x))
                    .Select(x => x)
                    .WithAttributes(
                        ActorAttributes.CreateDispatcher("my-dispatcher")
                            .And(Attributes.CreateName("seperate-dispatcher")));

            Source.From(new[] {0, 1, 2}).Via(f1).Via(f2).RunWith(Sink.Ignore<int>(), Materializer);

            defaultDispatcher.ReceiveN(3).ForEach(o => o.ToString().Should().Contain("akka.test.stream-dispatcher"));

            customDispatcher.ReceiveN(3).ForEach(o => o.ToString().Should().Contain("my-dispatcher"));
        }

        private static T SentThreadNameTo<T>(IActorRef probe, T element)
        {
            probe.Tell(Thread.CurrentThread.Name);
            return element;
        }

    }
}
