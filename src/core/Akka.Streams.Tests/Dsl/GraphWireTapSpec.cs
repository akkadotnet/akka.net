//-----------------------------------------------------------------------
// <copyright file="GraphWireTapSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class GraphWireTapSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphWireTapSpec(ITestOutputHelper helper)
            : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_WireTap_must_broadcast_to_the_tap()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var (tps, mps) = Source.From(Enumerable.Range(1, 2))                                                                             
                .WireTapMaterialized(this.SinkProbe<int>(), Keep.Right)                                                                             
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)                                                                             
                .Run(Materializer);

                tps.Request(2);
                await mps.RequestNextAsync(1);
                await mps.RequestNextAsync(2);
                tps.ExpectNext(1, 2);
                await mps.ExpectCompleteAsync();
                await tps.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_WireTap_must_drop_elements_while_the_tap_has_no_demand_buffering_up_to_one_element()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var (tps, mps) = Source.From(Enumerable.Range(1, 6))                                                                             
                .WireTapMaterialized(this.SinkProbe<int>(), Keep.Right)                                                                             
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)                                                                             
                .Run(Materializer);
                mps.Request(3);
                mps.ExpectNext(1, 2, 3);
                tps.Request(4);
                await mps.RequestNextAsync(4);
                await mps.RequestNextAsync(5);
                await mps.RequestNextAsync(6);
                tps.ExpectNext(3, 4, 5, 6);
                await mps.ExpectCompleteAsync();
                await tps.ExpectCompleteAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task A_WireTap_must_cancel_if_main_sink_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var (tps, mps) = Source.From(Enumerable.Range(1, 6))                                                                             
                .WireTapMaterialized(this.SinkProbe<int>(), Keep.Right)                                                                             
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)                                                                             
                .Run(Materializer);

                tps.Request(6);
                mps.Cancel();
                await tps.ExpectCompleteAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task A_WireTap_must_continue_if_tap_sink_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var (tps, mps) = Source.From(Enumerable.Range(1, 6))                                                                             
                .WireTapMaterialized(this.SinkProbe<int>(), Keep.Right)                                                                             
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)                                                                             
                .Run(Materializer);
                tps.Cancel();
                mps.Request(6);
                mps.ExpectNext(1, 2, 3, 4, 5, 6);
                await mps.ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
