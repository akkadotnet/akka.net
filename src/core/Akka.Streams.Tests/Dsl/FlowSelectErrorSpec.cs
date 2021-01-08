//-----------------------------------------------------------------------
// <copyright file="FlowSelectErrorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    // JVM - FlowMapErrorSpec
    public class FlowSelectErrorSpec : AkkaSpec
    {
        private static readonly Exception Exception = new Exception("ex");
        private static readonly Exception Boom = new Exception("BOOM!");

        public FlowSelectErrorSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            Materializer = Sys.Materializer(settings);
        }

        public ActorMaterializer Materializer { get; }

        [Fact]
        public void A_SelectError_must_select_when_there_is_a_handler()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3))
                    .Select(ThrowOnTwo)
                    .SelectError(_ => Boom)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(2)
                    .ExpectNext(1)
                    .ExpectError().Should().Be(Boom);
            }, Materializer);
        }

        [Fact]
        public void A_SelectError_must_fail_the_stream_with_exception_thrown_in_handler_and_log_it()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3))
                    .Select(ThrowOnTwo)
                    .SelectError(_ => throw Boom)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .RequestNext(1)
                    .Request(1)
                    .ExpectError().Should().Be(Boom);

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
        public void A_SelectError_must_not_influence_stream_when_there_is_no_exceptions()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3))
                    .Select(x => x)
                    .SelectError(ex => Boom)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .RequestNext(1)
                    .RequestNext(2)
                    .RequestNext(3)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_SelectError_must_finish_stream_if_it_is_empty()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>()
                    .Select(x => x)
                    .SelectError(_ => Boom)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(1)
                    .ExpectComplete();
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
