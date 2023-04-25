//-----------------------------------------------------------------------
// <copyright file="FlowOnCompleteSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
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
        public async Task A_Flow_with_OnComplete_must_invoke_callback_on_normal_completion()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var onCompleteProbe = CreateTestProbe();
                var p = this.CreateManualPublisherProbe<int>();
                Source.FromPublisher(p)
                    .To(Sink.OnComplete<int>(() => onCompleteProbe.Ref.Tell("done"), _ => { }))
                    .Run(Materializer);
                var proc = await p.ExpectSubscriptionAsync();
                await proc.ExpectRequestAsync();
                proc.SendNext(42);
                await onCompleteProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                proc.SendComplete();
                await onCompleteProbe.ExpectMsgAsync("done");
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_OnComplete_must_yield_the_first_error()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var onCompleteProbe = CreateTestProbe();
                var p = this.CreateManualPublisherProbe<int>();
                Source.FromPublisher(p)
                    .To(Sink.OnComplete<int>(() => { }, ex => onCompleteProbe.Ref.Tell(ex)))
                    .Run(Materializer);
                var proc = await p.ExpectSubscriptionAsync();
                await proc.ExpectRequestAsync();
                var cause = new TestException("test");
                proc.SendError(cause);
                await onCompleteProbe.ExpectMsgAsync(cause);
                await onCompleteProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_OnComplete_must_invoke_callback_for_an_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var onCompleteProbe = CreateTestProbe();
                var p = this.CreateManualPublisherProbe<int>();
                Source.FromPublisher(p)
                    .To(Sink.OnComplete<int>(() => onCompleteProbe.Ref.Tell("done"), _ => { }))
                    .Run(Materializer);
                var proc = await p.ExpectSubscriptionAsync();
                await proc.ExpectRequestAsync();
                proc.SendComplete();
                await onCompleteProbe.ExpectMsgAsync("done");
                await onCompleteProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_OnComplete_must_invoke_callback_after_transform_and_foreach_steps()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var onCompleteProbe = CreateTestProbe();
                var p = this.CreateManualPublisherProbe<int>();
                var foreachSink = Sink.ForEach<int>(x => onCompleteProbe.Ref.Tell("foreach-" + x));
                var future = Source.FromPublisher(p).Select(x =>
                {
                    onCompleteProbe.Ref.Tell("map-" + x);
                    return x;
                }).RunWith(foreachSink, Materializer);
                future.ContinueWith(t => onCompleteProbe.Tell(t.IsCompleted ? "done" : "failure"));

                var proc = await p.ExpectSubscriptionAsync();
                await proc.ExpectRequestAsync();
                proc.SendNext(42);
                proc.SendComplete();
                await onCompleteProbe.ExpectMsgAsync("map-42");
                await onCompleteProbe.ExpectMsgAsync("foreach-42");
                await onCompleteProbe.ExpectMsgAsync("done");
               
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_OnComplete_must_yield_error_on_abrupt_termination()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var onCompleteProbe = CreateTestProbe();
            var publisher = this.CreateManualPublisherProbe<int>();

            Source.FromPublisher(publisher).To(Sink.OnComplete<int>(() => onCompleteProbe.Ref.Tell("done"),
                    ex => onCompleteProbe.Ref.Tell(ex)))
                .Run(materializer);
            var proc = await publisher.ExpectSubscriptionAsync();
            await proc.ExpectRequestAsync();
            materializer.Shutdown();

            await onCompleteProbe.ExpectMsgAsync<AbruptTerminationException>();
        }
    }
}

