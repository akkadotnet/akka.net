using System;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class HeadSinkSpec : ScriptedTest
    {
        private ActorMaterializer Materializer { get; }

        public HeadSinkSpec(ITestOutputHelper helper):base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_FLow_with_a_Sink_Head_must_yield_the_first_value()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = TestPublisher.CreateManualProbe<int>(this);
                var task = Source.FromPublisher<int, Unit>(p).Map(x=>x).RunWith(Sink.First<int>(), Materializer);
                var proc = p.ExpectSubscription();
                proc.ExpectRequest();
                proc.SendNext(42);
                task.Wait(100);
                task.Result.Should().Be(42);
                proc.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void A_FLow_with_a_Sink_Head_must_yield_the_first_value_when_actively_constructing()
        {
            var p = TestPublisher.CreateManualProbe<int>(this);
            var f = Sink.First<int>();
            var s = Source.AsSubscriber<int>();
            var t = s.ToMaterialized(f, Keep.Both).Run(Materializer);
            var subscriber = t.Item1;
            var future = t.Item2;

            p.Subscribe(subscriber);
            var proc = p.ExpectSubscription();
            proc.ExpectRequest();
            proc.SendNext(42);
            future.Wait(100);
            future.Result.Should().Be(42);
            proc.ExpectCancellation();
        }

        [Fact]
        public void A_FLow_with_a_Sink_Head_must_yield_the_first_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Failed<int>(new SystemException("ex"))
                    .Invoking(s => s.RunWith(Sink.First<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))
                    .ShouldThrow<AggregateException>()
                    .WithInnerException<SystemException>()
                    .WithInnerMessage("ex");
            }, Materializer);
        }

        [Fact]
        public void A_FLow_with_a_Sink_Head_must_yield_NoSuchElementException_for_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .Invoking(s => s.RunWith(Sink.First<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))
                    .ShouldThrow<AggregateException>()
                    .WithInnerException<NoSuchElementException>()
                    .WithInnerMessage("First of empty stream");
            }, Materializer);
        }



        [Fact]
        public void A_FLow_with_a_Sink_HeadOption_must_yield_the_first_value()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p = TestPublisher.CreateManualProbe<int>(this);
                var task = Source.FromPublisher<int, Unit>(p).Map(x => x).RunWith(Sink.FirstOrDefault<int>(), Materializer);
                var proc = p.ExpectSubscription();
                proc.ExpectRequest();
                proc.SendNext(42);
                task.Wait(100);
                task.Result.Should().Be(42);
                proc.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void A_FLow_with_a_Sink_HeadOption_must_yield_the_first_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Failed<int>(new SystemException("ex"))
                    .Invoking(s => s.RunWith(Sink.FirstOrDefault<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))
                    .ShouldThrow<AggregateException>()
                    .WithInnerException<SystemException>()
                    .WithInnerMessage("ex");
            }, Materializer);
        }

        [Fact]
        public void A_FLow_with_a_Sink_HeadOption_must_yield_default_for_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.Empty<int>().RunWith(Sink.FirstOrDefault<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(1));
                task.Result.Should().Be(0);
            }, Materializer);
        }
    }
}
