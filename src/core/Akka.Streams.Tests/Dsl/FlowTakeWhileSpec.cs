//-----------------------------------------------------------------------
// <copyright file="FlowTakeWhileSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowTakeWhileSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowTakeWhileSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_TakeWhile_must_take_while_predicate_is_true()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 4))                                                                             
                .TakeWhile(i => i < 3)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(3)                                                                             
                .ExpectNext(1, 2)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_TakeWhile_must_complete_the_future_for_an_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.Empty<int>()                                                                             
                .TakeWhile(i => i < 2)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(1)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_TakeWhile_must_continue_if_error()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var testException = new Exception("test");

                await Source.From(Enumerable.Range(1, 4)).TakeWhile(a =>
                {
                    if (a == 3)
                        throw testException;
                    return true;
                })
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(4)
                    .ExpectNext(1, 2, 4)
                    .ExpectCompleteAsync();
                
            }, Materializer);
        }

        [Fact]
        public async Task A_TakeWhile_must_emit_the_element_that_caused_the_predicate_to_return_false_and_then_no_more_with_inclusive_set()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 10))                                                                         
                .TakeWhile(i => i < 3, true)                                                                         
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                         
                .Request(4)                                                                         
                .ExpectNext(1, 2, 3)                                                                         
                .ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
