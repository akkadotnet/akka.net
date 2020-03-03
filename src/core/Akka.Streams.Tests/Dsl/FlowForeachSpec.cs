//-----------------------------------------------------------------------
// <copyright file="FlowForeachSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowForeachSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowForeachSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void A_Foreach_must_call_the_procedure_for_each_element()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3)).RunForeach(i => TestActor.Tell(i), Materializer).ContinueWith(
                    task =>
                    {
                        if(task.IsCompleted && task.Exception == null)
                            TestActor.Tell("done");
                    });

                ExpectMsg(1);
                ExpectMsg(2);
                ExpectMsg(3);
                ExpectMsg("done");
            }, Materializer);
        }

        [Fact]
        public void A_Foreach_must_complete_the_future_for_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>().RunForeach(i => TestActor.Tell(i), Materializer).ContinueWith(
                    task =>
                    {
                        if (task.IsCompleted && task.Exception == null)
                            TestActor.Tell("done");
                    });
                ExpectMsg("done");
            }, Materializer);
        }

        [Fact]
        public void A_Foreach_must_yield_the_first_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = this.CreateManualPublisherProbe<int>();
                Source.FromPublisher(p).RunForeach(i => TestActor.Tell(i), Materializer).ContinueWith(task =>
                {
                    if (task.Exception != null)
                        TestActor.Tell(task.Exception.InnerException);
                });
                var proc = p.ExpectSubscription();
                var ex = new TestException("ex");
                proc.SendError(ex);
                ExpectMsg(ex);
            }, Materializer);
        }

        [Fact]
        public void A_Foreach_must_complete_future_with_failure_when_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var error = new TestException("test");
                var future = Source.Single(1).RunForeach(_ =>
                {
                    throw error;
                }, Materializer);

                future.Invoking(f => f.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<TestException>()
                    .And.Should()
                    .Be(error);
            }, Materializer);
        }
    }
}
