//-----------------------------------------------------------------------
// <copyright file="LastSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

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
        public async Task A_Flow_with_Sink_Last_must_yield_the_last_value()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var result = await Source.From(Enumerable.Range(1,42)).Select(x=>x)
                    .RunWith(Sink.Last<int>(), Materializer)
                    .ShouldCompleteWithin(3.Seconds());
                result.Should().Be(42);
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_Sink_Last_must_yield_the_first_error()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                (await Source.Failed<int>(new Exception("ex")).RunWith(Sink.Last<int>(), Materializer)
                    .ShouldThrowWithin<Exception>(1.Seconds())).Message.Should().Be("ex");
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_Sink_Last_must_yield_NoSuchElementException_for_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                (await Source.Empty<int>().RunWith(Sink.Last<int>(), Materializer)
                    .ShouldThrowWithin<NoSuchElementException>(1.Seconds()))
                    .Message.Should().Be("Last of empty stream");
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_Sink_LastOption_must_yield_the_last_value()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var result = await Source.From(Enumerable.Range(1, 42)).Select(x => x)
                    .RunWith(Sink.LastOrDefault<int>(), Materializer)
                    .ShouldCompleteWithin(1.Seconds());
                result.Should().Be(42);
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_Sink_LastOption_must_yield_the_first_error()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                (await Source.Failed<int>(new Exception("ex")).RunWith(Sink.LastOrDefault<int>(), Materializer)
                    .ShouldThrowWithin<Exception>(1.Seconds())).Message.Should().Be("ex");
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_with_Sink_LastOption_must_yield_default_for_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var result = await Source.Empty<int>()
                    .RunWith(Sink.LastOrDefault<int>(), Materializer)
                    .ShouldCompleteWithin(1.Seconds());
                result.Should().Be(0);
            }, Materializer);
        }
    }
}
