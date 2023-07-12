//-----------------------------------------------------------------------
// <copyright file="FlowRecoverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowRecoverSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowRecoverSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static readonly TestException Ex = new("test");

        [Fact]
        public async Task A_Recover_must_recover_when_there_is_a_handler()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 4)).Select(x =>                                                                         
                {                                                                             
                    if (x == 3)                                                                                 
                        throw Ex;                                                                             
                    return x;                                                                         
                })                                                                             
                .Recover(_ => Option<int>.Create(0))                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .RequestNext(1)                                                                             
                .RequestNext(2)                                                                             
                .RequestNext(0)                                                                             
                .Request(1)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Recover_must_failed_stream_if_handler_is_not_for_such_exception_type()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                Source.From(Enumerable.Range(1, 3)).Select(x =>                                                                         
                {                                                                             
                    if (x == 2)                                                                                 
                        throw Ex;                                                                             
                    return x;                                                                         
                })                                                                             
                .Recover(_ => Option<int>.None)                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .RequestNext(1)                                                                             
                .Request(1)                                                                             
                .ExpectError().Should().Be(Ex);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Recover_must_not_influence_stream_when_there_is_no_exception()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await Source.From(Enumerable.Range(1, 3))                                                                             
                .Select(x => x)                                                                             
                .Recover(_ => Option<int>.Create(0))                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(3)                                                                             
                .ExpectNext(1, 2, 3)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Recover_must_finish_stream_if_it_is_empty()
        {
           await this.AssertAllStagesStoppedAsync(async() => {
                await Source.Empty<int>()                                                                             
                .Select(x => x)                                                                             
                .Recover(_ => Option<int>.Create(0))                                                                             
                .RunWith(this.SinkProbe<int>(), Materializer)                                                                             
                .Request(1)                                                                             
                .ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
