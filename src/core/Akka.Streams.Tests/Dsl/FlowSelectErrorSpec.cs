//-----------------------------------------------------------------------
// <copyright file="FlowSelectErrorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    // JVM - FlowMapErrorSpec
    public class FlowSelectErrorSpec : AkkaSpec
    {
        private static readonly Exception Exception = new("ex");
        private static readonly Exception Boom = new("BOOM!");

        public FlowSelectErrorSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            Materializer = Sys.Materializer(settings);
        }

        public ActorMaterializer Materializer { get; }

        [Fact]
        public async Task A_SelectError_must_select_when_there_is_a_handler()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                Source.From(Enumerable.Range(1, 3))                                                                             
                .Select(ThrowOnTwo)                                                                             
                .SelectError(_ => Boom)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(2)                                                                             
                .ExpectNext(1)                                                                             
                .ExpectError().Should().Be(Boom);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_SelectError_must_fail_the_stream_with_exception_thrown_in_handler_and_log_it()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                Source.From(Enumerable.Range(1, 3))                                                                             
                .Select(ThrowOnTwo)                                                                             
                .SelectError(_ => throw Boom)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .RequestNext(1)                                                                             
                .Request(1)                                                                             
                .ExpectError().Should().Be(Boom);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public void A_SelectError_must_pass_through_the_original_exception_if_function_doesn_not_handle_it()
        {
            // actually we don't need this spec since the selector is always used 
            // because we can't use "if (f.isDefinedAt(ex))"
            // and another predicate parameter seems to be odd

            Source.From(Enumerable.Range(1, 3))
                .Select(ThrowOnTwo)
                .SelectError(ex => ex is IndexOutOfRangeException ? Boom : ex)
                .RunWith(this.SinkProbe<int>(), Materializer)
                .RequestNext(1)
                .Request(1)
                .ExpectError().Should().Be(Exception);
        }

        [Fact]
        public async Task A_SelectError_must_not_influence_stream_when_there_is_no_exceptions()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 3))                                                                             
                .Select(x => x)                                                                             
                .SelectError(_ => Boom)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .RequestNext(1)                                                                             
                .RequestNext(2)                                                                             
                .RequestNext(3)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_SelectError_must_finish_stream_if_it_is_empty()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.Empty<int>()                                                                             
                .Select(x => x)                                                                             
                .SelectError(_ => Boom)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(1)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        private static int ThrowOnTwo(int i)
        {
            if (i == 2)
                throw Exception;

            return i;
        }
    }
}
