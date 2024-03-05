//-----------------------------------------------------------------------
// <copyright file="FlowSkipWhileSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    //JVM: FlowDropWhileSpec
    public class FlowSkipWhileSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSkipWhileSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_SkipWhile_must_skip_while_predicate_is_true()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 4))                                                                             
                .SkipWhile(x => x < 3)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(2)                                                                             
                .ExpectNext(3, 4)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_SkipWhile_must_complete_the_future_for_an_empty_stream()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.Empty<int>()                                                                             
                .SkipWhile(x => x < 2)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(1)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_SkipWhile_must_continue_if_error()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 4)).SkipWhile(x =>                                                                         
                {                                                                             
                    if (x < 3)                                                                                 
                        return true;                                                                             
                    throw new TestException("");                                                                         
                })                                                                             
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(1)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_SkipWhile_must_restart_with_strategy()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 4)).SkipWhile(x =>                                                                         
                {                                                                             
                    if (x is 1 or 3)                                                                                 
                        return true;                                                                             
                    if (x == 4)                                                                                 
                        return false;                                                                             
                    throw new TestException("");                                                                         
                })                                                                             
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(1)                                                                             
                .ExpectNext(4)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
