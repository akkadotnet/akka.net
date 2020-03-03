//-----------------------------------------------------------------------
// <copyright file="LastSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class LastSinkSpec : ScriptedTest
    {
        private ActorMaterializer Materializer { get; }

        public LastSinkSpec(ITestOutputHelper helper):base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Flow_with_Sink_Last_must_yield_the_last_value()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1,42)).Select(x=>x).RunWith(Sink.Last<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                task.Result.Should().Be(42);
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_Sink_Last_must_yield_the_first_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Failed<int>(new Exception("ex"))
                    .Invoking(s => s.RunWith(Sink.Last<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))
                    .ShouldThrow<AggregateException>()
                    .WithInnerException<Exception>()
                    .WithInnerMessage("ex");
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_Sink_Last_must_yield_NoSuchElementException_for_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .Invoking(s => s.RunWith(Sink.Last<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))
                    .ShouldThrow<AggregateException>()
                    .WithInnerException<NoSuchElementException>()
                    .WithInnerMessage("Last of empty stream");
            }, Materializer);
        }


        [Fact]
        public void A_Flow_with_Sink_LastOption_must_yield_the_last_value()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 42)).Select(x => x).RunWith(Sink.LastOrDefault<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                task.Result.Should().Be(42);
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_Sink_LastOption_must_yield_the_first_error()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Failed<int>(new Exception("ex"))
                    .Invoking(s => s.RunWith(Sink.LastOrDefault<int>(), Materializer).Wait(TimeSpan.FromSeconds(1)))
                    .ShouldThrow<AggregateException>()
                    .WithInnerException<Exception>()
                    .WithInnerMessage("ex");
            }, Materializer);
        }

        [Fact]
        public void A_Flow_with_Sink_LastOption_must_yield_default_for_empty_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.Empty<int>().RunWith(Sink.LastOrDefault<int>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                task.Result.Should().Be(0);
            }, Materializer);
        }
    }
}
