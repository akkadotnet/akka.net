//-----------------------------------------------------------------------
// <copyright file="HeadSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
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
        public async Task A_FLow_with_a_Sink_Head_must_yield_the_first_value()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = this.CreateManualPublisherProbe<int>();
                var task = Source.FromPublisher(p).Select(x => x).RunWith(Sink.First<int>(), Materializer);
                var proc = await p.ExpectSubscriptionAsync();
                await proc.ExpectRequestAsync();
                proc.SendNext(42);
                task.Wait(100);
                task.Result.Should().Be(42);
                await proc.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_FLow_with_a_Sink_Head_must_yield_the_first_value_when_actively_constructing()
        {
            var p = this.CreateManualPublisherProbe<int>();
            var f = Sink.First<int>();
            var s = Source.AsSubscriber<int>();
            var t = s.ToMaterialized(f, Keep.Both).Run(Materializer);
            var subscriber = t.Item1;
            var future = t.Item2;

            p.Subscribe(subscriber);
            var proc = await p.ExpectSubscriptionAsync();
            await proc.ExpectRequestAsync();
            proc.SendNext(42);
            future.Wait(100);
            future.Result.Should().Be(42);
            await proc.ExpectCancellationAsync();
        }

        [Fact]
        public async Task A_FLow_with_a_Sink_Head_must_yield_the_first_error()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                Source.Failed<int>(new Exception("ex"))                                                                             
                .Invoking(s => s.RunWith(Sink.First<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))                                                                             
                .Should().Throw<AggregateException>()                                                                             
                .WithInnerException<Exception>()                                                                            
                .WithMessage("ex");
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_FLow_with_a_Sink_Head_must_yield_NoSuchElementException_for_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                Source.Empty<int>()                                                                             
                .Invoking(s => s.RunWith(Sink.First<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))                                                                             
                .Should().Throw<AggregateException>()                                                                             
                .WithInnerException<NoSuchElementException>()                                                                             
                .WithMessage("First of empty stream");
                return Task.CompletedTask;
            }, Materializer);
        }



        [Fact]
        public async Task A_FLow_with_a_Sink_HeadOption_must_yield_the_first_value()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = this.CreateManualPublisherProbe<int>();
                var task = Source.FromPublisher(p).Select(x => x).RunWith(Sink.FirstOrDefault<int>(), Materializer);
                var proc = await p.ExpectSubscriptionAsync();
                await proc.ExpectRequestAsync();
                proc.SendNext(42);
                task.Wait(100);
                task.Result.Should().Be(42);
                await proc.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_FLow_with_a_Sink_HeadOption_must_yield_the_first_error()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                Source.Failed<int>(new Exception("ex"))                                                                             
                .Invoking(s => s.RunWith(Sink.FirstOrDefault<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))                                                                             
                .Should().Throw<AggregateException>()                                                                             
                .WithInnerException<Exception>()                                                                             
                .WithMessage("ex");
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_FLow_with_a_Sink_HeadOption_must_yield_default_for_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var task = Source.Empty<int>().RunWith(Sink.FirstOrDefault<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                task.Result.Should().Be(0);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public void A_FLow_with_a_Sink_HeadOption_must_fail_on_abrupt_termination()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var source = this.CreatePublisherProbe<int>();
            var task = Source.FromPublisher(source).RunWith(Sink.FirstOrDefault<int>(), materializer);

            materializer.Shutdown();

            // this one always fails with the AbruptTerminationException rather than the
            // AbruptStageTerminationException for some reason
            Action a = () => task.Wait(TimeSpan.FromSeconds(3));
            a.Should().Throw<AbruptTerminationException>();
        }
    }
}
