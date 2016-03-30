using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Threading;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowConflateSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowConflateSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void Conflate_must_pass_through_elements_unchanged_when_there_is_no_rate_difference()
        {
            var publisher = TestPublisher.CreateProbe<int>(this);
            var subscriber = TestSubscriber.CreateManualProbe<int>(this);

            Source.FromPublisher<int, Unit>(publisher)
                .ConflateWithSeed(i => i, (sum, i) => sum + i)
                .To(Sink.FromSubscriber<int, Unit>(subscriber))
                .Run(Materializer);
            var sub = subscriber.ExpectSubscription();

            for (var i = 1; i <= 100; i++)
            {
                sub.Request(1);
                publisher.SendNext(i);
                subscriber.ExpectNext(i);
            }

            sub.Cancel();
        }

        [Fact]
        public void Conflate_must_pass_through_elements_unchanged_when_there_is_no_rate_difference_simple_conflate()
        {
            var publisher = TestPublisher.CreateProbe<int>(this);
            var subscriber = TestSubscriber.CreateManualProbe<int>(this);

            Source.FromPublisher<int, Unit>(publisher)
                .Conflate((sum, i) => sum + i)
                .To(Sink.FromSubscriber<int, Unit>(subscriber))
                .Run(Materializer);
            var sub = subscriber.ExpectSubscription();

            for (var i = 1; i <= 100; i++)
            {
                sub.Request(1);
                publisher.SendNext(i);
                subscriber.ExpectNext(i);
            }

            sub.Cancel();
        }

        [Fact]
        public void Conflate_must_conflate_elements_while_downstream_is_silent()
        {
            var publisher = TestPublisher.CreateProbe<int>(this);
            var subscriber = TestSubscriber.CreateManualProbe<int>(this);

            Source.FromPublisher<int, Unit>(publisher)
                .ConflateWithSeed(i=>i,(sum, i) => sum + i)
                .To(Sink.FromSubscriber<int, Unit>(subscriber))
                .Run(Materializer);
            var sub = subscriber.ExpectSubscription();

            for (var i = 1; i <= 100; i++)
                publisher.SendNext(i);

            subscriber.ExpectNoMsg(TimeSpan.FromSeconds(1));
            sub.Request(1);
            subscriber.ExpectNext(5050);
            
            sub.Cancel();
        }

        [Fact]
        public void Conflate_must_conflate_elements_while_downstream_is_silent_simple_conflate()
        {
            var publisher = TestPublisher.CreateProbe<int>(this);
            var subscriber = TestSubscriber.CreateManualProbe<int>(this);

            Source.FromPublisher<int, Unit>(publisher)
                .Conflate((sum, i) => sum + i)
                .To(Sink.FromSubscriber<int, Unit>(subscriber))
                .Run(Materializer);
            var sub = subscriber.ExpectSubscription();

            for (var i = 1; i <= 100; i++)
                publisher.SendNext(i);

            subscriber.ExpectNoMsg(TimeSpan.FromSeconds(1));
            sub.Request(1);
            subscriber.ExpectNext(5050);

            sub.Cancel();
        }

        [Fact]
        public void Conflate_must_work_on_a_variable_rate_chain()
        {
            var future = Source.From(Enumerable.Range(1, 1000)).ConflateWithSeed(i => i, (sum, i) => sum + i).Map(i =>
            {
                if (ThreadLocalRandom.Current.Next(1, 3) == 2)
                    Thread.Sleep(10);
                return i;
            }).RunFold(0, (sum, i) => sum + i, Materializer);
            future.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            future.Result.Should().Be(500500);
        }

        [Fact]
        public void Conflate_must_work_on_a_variable_rate_chain_simple_conflate()
        {
            var future = Source.From(Enumerable.Range(1, 1000)).Conflate((sum, i) => sum + i).Map(i =>
            {
                if (ThreadLocalRandom.Current.Next(1, 3) == 2)
                    Thread.Sleep(10);
                return i;
            }).RunFold(0, (sum, i) => sum + i, Materializer);
            future.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            future.Result.Should().Be(500500);
        }

        [Fact]
        public void Conflate_must_backpressure_subscriber_when_upstream_is_slower()
        {
            var publisher = TestPublisher.CreateProbe<int>(this);
            var subscriber = TestSubscriber.CreateManualProbe<int>(this);

            Source.FromPublisher<int, Unit>(publisher)
                .ConflateWithSeed(i=>i, (sum, i) => sum + i)
                .To(Sink.FromSubscriber<int, Unit>(subscriber))
                .Run(Materializer);
            var sub = subscriber.ExpectSubscription();

            sub.Request(1);
            publisher.SendNext(1);
            subscriber.ExpectNext(1);

            sub.Request(1);
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            publisher.SendNext(2);
            subscriber.ExpectNext(2);

            publisher.SendNext(3);
            publisher.SendNext(4);
            // The request can be in race with the above onNext(4) so the result would be either 3 or 7.
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            sub.Request(1);
            subscriber.ExpectNext(7);

            sub.Request(1);
            subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            sub.Cancel();
        }

        [Fact]
        public void Conflate_must_work_with_a_buffer_and_fold()
        {
            var future =
                Source.From(Enumerable.Range(1, 50))
                    .ConflateWithSeed(i => i, (sum, i) => sum + i)
                    .Buffer(50, OverflowStrategy.Backpressure)
                    .RunFold(0, (sum, i) => sum + i, Materializer);
            future.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            future.Result.Should().Be(Enumerable.Range(1, 50).Sum());
        }

        [Fact]
        public void Conflate_must_restart_when_seed_throws_and_a_RestartDescider_is_used()
        {
            var sourceProbe = TestPublisher.CreateProbe<string>(this);
            var sinkProbe = TestSubscriber.CreateProbe<string>(this);
            var latch = new TestLatch();

            var conflate = Flow.Create<string>().ConflateWithSeed(i => i, (state, elem) =>
            {
                if (elem == "two")
                {
                    latch.Open();
                    throw new TestException("two is a three letter word");
                }

                return state + elem;
            }).WithAttributes(Attributes.CreateSupervisionStrategy(Deciders.RestartingDecider));

            var graph = Source.FromPublisher<string, Unit>(sourceProbe)
                .Via(conflate)
                .To(Sink.FromSubscriber<string, Unit>(sinkProbe))
                .WithAttributes(Attributes.CreateInputBuffer(4, 4));
            RunnableGraph<Unit>.FromGraph(graph).Run(Materializer);

            var sub = sourceProbe.ExpectSubscription();

            sub.ExpectRequest(4);
            sub.SendNext("one");
            sub.SendNext("two");
            sub.SendNext("three");
            sub.SendComplete();

            //"one" should be lost
            latch.Ready(TimeSpan.FromSeconds(3));
            sinkProbe.RequestNext("three");
        }

        [Fact]
        public void Conflate_must_restart_when_aggregate_throws_and_a_RestartingDecider_is_used()
        {
            var sourceProbe = TestPublisher.CreateProbe<int>(this);
            var sinkProbe = TestSubscriber.CreateManualProbe<List<int>>(this);
            var saw4Latch = new TestLatch();

            var graph = Source.FromPublisher<int, Unit>(sourceProbe).ConflateWithSeed(i=>new List<int> {i},
                (state, elem) =>
                {
                    if(elem == 2)
                        throw new TestException("three is a four letter word");
                    
                    if(elem == 4)
                        saw4Latch.Open();

                    state.Add(elem);
                    return state;
                })
                .WithAttributes(Attributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .To(Sink.FromSubscriber<List<int>, Unit>(sinkProbe))
                .WithAttributes(Attributes.CreateInputBuffer(1, 1));
            RunnableGraph<Unit>.FromGraph(graph).Run(Materializer);

            var sub = sourceProbe.ExpectSubscription();
            var sinkSub = sinkProbe.ExpectSubscription();

            // push the first three values, the third will trigger
            // the exception
            sub.ExpectRequest(1);
            sub.SendNext(1);

            // causing the 1 to get thrown away
            sub.ExpectRequest(1);
            sub.SendNext(2);

            sub.ExpectRequest(1);
            sub.SendNext(3);

            sub.ExpectRequest(1);
            sub.SendNext(3);

            // and consume it, so that the next element
            // will trigger seed
            saw4Latch.Ready(TimeSpan.FromSeconds(3));
            sinkSub.Request(1);

            sinkProbe.ExpectNext(new List<int> {1, 3, 4});
        }

        [Fact]
        public void Conflate_must_restart_when_aggregate_throws_and_a_ResumingDecider_is_used()
        {
            var sourceProbe = TestPublisher.CreateProbe<int>(this);
            var sinkProbe = TestSubscriber.CreateManualProbe<List<int>>(this);
            var saw4Latch = new TestLatch();

            var graph = Source.FromPublisher<int, Unit>(sourceProbe).ConflateWithSeed(i => new List<int> { i },
                (state, elem) =>
                {
                    if (elem == 2)
                        throw new TestException("three is a four letter word");

                    if (elem == 4)
                        saw4Latch.Open();

                    state.Add(elem);
                    return state;
                })
                .WithAttributes(Attributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .To(Sink.FromSubscriber<List<int>, Unit>(sinkProbe))
                .WithAttributes(Attributes.CreateInputBuffer(1, 1));
            RunnableGraph<Unit>.FromGraph(graph).Run(Materializer);

            var sub = sourceProbe.ExpectSubscription();
            var sinkSub = sinkProbe.ExpectSubscription();

            // push the first three values, the third will trigger
            // the exception
            sub.ExpectRequest(1);
            sub.SendNext(1);

            // causing the 1 to get thrown away
            sub.ExpectRequest(1);
            sub.SendNext(2);

            sub.ExpectRequest(1);
            sub.SendNext(3);

            sub.ExpectRequest(1);
            sub.SendNext(3);

            // and consume it, so that the next element
            // will trigger seed
            saw4Latch.Ready(TimeSpan.FromSeconds(3));
            sinkSub.Request(1);

            sinkProbe.ExpectNext(new List<int> { 1, 3, 4 });
        }
    }
}
