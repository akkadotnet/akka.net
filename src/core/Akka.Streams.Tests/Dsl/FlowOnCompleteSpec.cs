//-----------------------------------------------------------------------
// <copyright file="FlowOnCompleteSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowOnCompleteSpec : ScriptedTest
    {
        private ActorMaterializer Materializer { get; }

        public FlowOnCompleteSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Flow_with_OnComplete_must_invoke_callback_on_normal_completion()
        {
            this.AssertAllStagesStopped(() =>
            {
                var onCompleteProbe = CreateTestProbe();
                var p = this.CreateManualPublisherProbe<int>();
                Source.FromPublisher(p)
                    .To(Sink.OnComplete<int>(() => onCompleteProbe.Ref.Tell("done"), _ => { }))
                    .Run(Materializer);
                var proc = p.ExpectSubscription();
                proc.ExpectRequest();
                proc.SendNext(42);
                onCompleteProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                proc.SendComplete();
                onCompleteProbe.ExpectMsg("done");
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_OnComplete_must_yield_the_first_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                var onCompleteProbe = CreateTestProbe();
                var p = this.CreateManualPublisherProbe<int>();
                Source.FromPublisher(p)
                    .To(Sink.OnComplete<int>(() => { }, ex => onCompleteProbe.Ref.Tell(ex)))
                    .Run(Materializer);
                var proc = p.ExpectSubscription();
                proc.ExpectRequest();
                var cause = new TestException("test");
                proc.SendError(cause);
                onCompleteProbe.ExpectMsg(cause);
                onCompleteProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_OnComplete_must_invoke_callback_for_an_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var onCompleteProbe = CreateTestProbe();
                var p = this.CreateManualPublisherProbe<int>();
                Source.FromPublisher(p)
                    .To(Sink.OnComplete<int>(() => onCompleteProbe.Ref.Tell("done"), _ => { }))
                    .Run(Materializer);
                var proc = p.ExpectSubscription();
                proc.ExpectRequest();
                proc.SendComplete();
                onCompleteProbe.ExpectMsg("done");
                onCompleteProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_OnComplete_must_invoke_callback_after_transform_and_foreach_steps()
        {
            this.AssertAllStagesStopped(() =>
            {
                var onCompleteProbe = CreateTestProbe();
                var p = this.CreateManualPublisherProbe<int>();
                var foreachSink = Sink.ForEach<int>(x => onCompleteProbe.Ref.Tell("foreach-" + x));
                var future = Source.FromPublisher(p).Select(x =>
                {
                    onCompleteProbe.Ref.Tell("map-" + x);
                    return x;
                }).RunWith(foreachSink, Materializer);
                future.ContinueWith(t => onCompleteProbe.Tell(t.IsCompleted ? "done" : "failure"));

                var proc = p.ExpectSubscription();
                proc.ExpectRequest();
                proc.SendNext(42);
                proc.SendComplete();
                onCompleteProbe.ExpectMsg("map-42");
                onCompleteProbe.ExpectMsg("foreach-42");
                onCompleteProbe.ExpectMsg("done");
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_OnComplete_must_yield_error_on_abrupt_termination()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var onCompleteProbe = CreateTestProbe();
            var publisher = this.CreateManualPublisherProbe<int>();

            Source.FromPublisher(publisher).To(Sink.OnComplete<int>(() => onCompleteProbe.Ref.Tell("done"),
                    ex => onCompleteProbe.Ref.Tell(ex)))
                .Run(materializer);
            var proc = publisher.ExpectSubscription();
            proc.ExpectRequest();
            materializer.Shutdown();

            onCompleteProbe.ExpectMsg<AbruptTerminationException>();
        }
    }
}

