//-----------------------------------------------------------------------
// <copyright file="GraphWireTapSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
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
        public void A_WireTap_must_broadcast_to_the_tap()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (tps, mps) = Source.From(Enumerable.Range(1, 2))
                    .WireTapMaterialized(this.SinkProbe<int>(), Keep.Right)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                tps.Request(2);
                mps.RequestNext(1);
                mps.RequestNext(2);
                tps.ExpectNext(1, 2);
                mps.ExpectComplete();
                tps.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_WireTap_must_drop_elements_while_the_tap_has_no_demand_buffering_up_to_one_element()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (tps, mps) = Source.From(Enumerable.Range(1, 6))
                    .WireTapMaterialized(this.SinkProbe<int>(), Keep.Right)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);

                mps.Request(3);
                mps.ExpectNext(1, 2, 3);
                tps.Request(4);
                mps.RequestNext(4);
                mps.RequestNext(5);
                mps.RequestNext(6);
                tps.ExpectNext(3, 4, 5, 6);
                mps.ExpectComplete();
                tps.ExpectComplete();
            }, Materializer);
        }
        
        [Fact]
        public void A_WireTap_must_cancel_if_main_sink_cancels()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (tps, mps) = Source.From(Enumerable.Range(1, 6))
                    .WireTapMaterialized(this.SinkProbe<int>(), Keep.Right)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                
                tps.Request(6);
                mps.Cancel();
                tps.ExpectComplete();
            }, Materializer);
        }
        
        [Fact]
        public void A_WireTap_must_continue_if_tap_sink_cancels()
        {
            this.AssertAllStagesStopped(() =>
            {
                var (tps, mps) = Source.From(Enumerable.Range(1, 6))
                    .WireTapMaterialized(this.SinkProbe<int>(), Keep.Right)
                    .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                    .Run(Materializer);
                
                tps.Cancel();
                mps.Request(6);
                mps.ExpectNext(1, 2, 3, 4, 5, 6);
                mps.ExpectComplete();
            }, Materializer);
        }
    }
}