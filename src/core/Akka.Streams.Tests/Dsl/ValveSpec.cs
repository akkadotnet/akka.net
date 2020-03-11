//-----------------------------------------------------------------------
// <copyright file="ValveSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class ValveSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public void Closed_Valve_should_emit_only_3_elements_into_a_sequence_when_the_valve_is_switched_to_open()
        {
            var t = Source.From(Enumerable.Range(1, 3))
                .ViaMaterialized(new Valve<int>(SwitchMode.Close), Keep.Right)
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var switchTask = t.Item1;
            var seq = t.Item2;

            var valveSwitch = switchTask.AwaitResult();
            Thread.Sleep(100);
            var flip = valveSwitch.Flip(SwitchMode.Open);
            flip.AwaitResult().Should().BeTrue();
        }

        [Fact]
        public void Closed_Valve_should_emit_only_5_elements_when_the_valve_is_switched_to_open()
        {
            var t = Source.From(Enumerable.Range(1, 5))
                .ViaMaterialized(new Valve<int>(SwitchMode.Close), Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var switchTask = t.Item1;
            var probe = t.Item2;

            var valveSwitch = switchTask.AwaitResult();
            probe.Request(2);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            var flip = valveSwitch.Flip(SwitchMode.Open);
            flip.AwaitResult().Should().BeTrue();

            probe.ExpectNext(1, 2);

            probe.Request(3);
            probe.ExpectNext(3, 4, 5);

            probe.ExpectComplete();
        }

        [Fact]
        public void Closed_Valve_should_emit_only_3_elements_when_the_valve_is_switch_to_open_close_open()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(new Valve<int>(SwitchMode.Close), Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var sourceProbe = t.Item1.Item1;
            var switchTask = t.Item1.Item2;
            var sinkProbe = t.Item2;

            var valveSwitch = switchTask.AwaitResult();

            sinkProbe.Request(1);
            var flip = valveSwitch.Flip(SwitchMode.Open);
            flip.AwaitResult().Should().BeTrue();

            sourceProbe.SendNext(1);

            sinkProbe.ExpectNext().Should().Be(1);

            flip = valveSwitch.Flip(SwitchMode.Close);
            flip.AwaitResult().Should().BeTrue();

            sinkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            flip = valveSwitch.Flip(SwitchMode.Open);
            flip.AwaitResult().Should().BeTrue();

            sinkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            sinkProbe.Request(1);
            sinkProbe.Request(1);
            sourceProbe.SendNext(2);
            sourceProbe.SendNext(3);
            sourceProbe.SendComplete();

            sinkProbe.ExpectNext(2, 3);

            sinkProbe.ExpectComplete();
        }

        [Fact]
        public void Closed_Valve_should_return_false_when_the_valve_is_already_closed()
        {
            var t = Source.From(Enumerable.Range(1, 5))
                .ViaMaterialized(new Valve<int>(SwitchMode.Close), Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var switchTask = t.Item1;
            var probe = t.Item2;

            var valveSwitch = switchTask.AwaitResult();

            valveSwitch.Flip(SwitchMode.Close).AwaitResult().Should().BeFalse();
            valveSwitch.Flip(SwitchMode.Close).AwaitResult().Should().BeFalse();
        }

        [Fact]
        public void Closed_Valve_should_emit_nothing_when_the_source_is_empty()
        {
            var t = Source.Empty<int>()
                .ViaMaterialized(new Valve<int>(SwitchMode.Close), Keep.Right)
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var seq = t.Item2;

            seq.AwaitResult().Should().BeEmpty();
        }

        [Fact]
        public void Closed_Valve_should_emit_nothing_when_the_source_is_failing()
        {
            var ex = new Exception();
            var t = Source.Failed<int>(ex)
                .ViaMaterialized(new Valve<int>(SwitchMode.Close), Keep.Right)
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var seq = t.Item2;

            seq.Invoking(x => x.AwaitResult()).ShouldThrow<Exception>().And.Should().Be(ex);
        }

        [Fact]
        public void Closed_Valve_should_not_pull_elements_again_when_opened_and_closed_and_re_opened()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(new Valve<int>(SwitchMode.Close), Keep.Both)
                .ToMaterialized(Sink.First<int>(), (l, r) => (l.Item1, l.Item2, r))
                .Run(Sys.Materializer());

            var probe = t.Item1;
            var switchTask = t.Item2;
            var resultTask = t.Item3;

            var valveSwitch = switchTask.AwaitResult();

            async Task<int> result()
            {
                await valveSwitch.Flip(SwitchMode.Open);
                await valveSwitch.Flip(SwitchMode.Close);
                await valveSwitch.Flip(SwitchMode.Open);
                probe.SendNext(1);
                probe.SendComplete();

                return await resultTask;
            }

            result().AwaitResult().Should().Be(1);
        }

        [Fact]
        public void Closed_Valve_should_be_in_closed_state()
        {
            var t = Source.From(Enumerable.Range(1, 3))
                .ViaMaterialized(new Valve<int>(SwitchMode.Close), Keep.Right)
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var switchTask = t.Item1;
            var seq = t.Item2;

            var valveSwitch = switchTask.AwaitResult();
            var mode = valveSwitch.GetMode().AwaitResult();
            mode.Should().Be(SwitchMode.Close);
        }

        [Fact]
        public void Open_Valve_should_emit_5_elements_after_it_has_been_close_open()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(new Valve<int>(), Keep.Both)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var sourceProbe = t.Item1.Item1;
            var switchTask = t.Item1.Item2;
            var sinkProbe = t.Item2;

            var valveSwitch = switchTask.AwaitResult();

            sinkProbe.Request(1);
            var flip = valveSwitch.Flip(SwitchMode.Close);
            flip.AwaitResult().Should().BeTrue();

            sourceProbe.SendNext(1);
            sinkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            flip = valveSwitch.Flip(SwitchMode.Open);
            flip.AwaitResult().Should().BeTrue();

            sinkProbe.ExpectNext().Should().Be(1);

            flip = valveSwitch.Flip(SwitchMode.Close);
            flip.AwaitResult().Should().BeTrue();

            flip = valveSwitch.Flip(SwitchMode.Open);
            flip.AwaitResult().Should().BeTrue();

            sinkProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            sinkProbe.Request(1);
            sinkProbe.Request(1);
            sourceProbe.SendNext(2);
            sourceProbe.SendNext(3);
            sourceProbe.SendComplete();

            sinkProbe.ExpectNext(2, 3);

            sinkProbe.ExpectComplete();
        }

        [Fact]
        public void Open_Valve_should_return_false_when_the_valve_is_already_opened()
        {
            var t = Source.From(Enumerable.Range(1, 5))
                .ViaMaterialized(new Valve<int>(), Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var switchTask = t.Item1;

            var valveSwitch = switchTask.AwaitResult();

            valveSwitch.Flip(SwitchMode.Open).AwaitResult().Should().BeFalse();
            valveSwitch.Flip(SwitchMode.Open).AwaitResult().Should().BeFalse();
        }

        [Fact]
        public void Open_Valve_should_emit_only_3_elements_into_a_sequence()
        {
            var t = Source.From(Enumerable.Range(1, 3))
                .ViaMaterialized(new Valve<int>(), Keep.Right)
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var seq = t.Item2;

            seq.AwaitResult(TimeSpan.FromMilliseconds(200)).Should().ContainInOrder(1, 2, 3);
        }

        [Fact]
        public void Open_Valve_should_emit_nothing_when_the_source_is_empty()
        {
            var t = Source.Empty<int>()
                .ViaMaterialized(new Valve<int>(), Keep.Right)
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var seq = t.Item2;

            seq.AwaitResult().Should().BeEmpty();
        }

        [Fact]
        public void Open_Valve_should_emit_nothing_when_the_source_is_failing()
        {
            var ex = new Exception();

            var t = Source.Failed<int>(ex)
                .ViaMaterialized(new Valve<int>(), Keep.Right)
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var seq = t.Item2;

            seq.Invoking(x => x.AwaitResult()).ShouldThrow<Exception>().And.Should().Be(ex);
        }

        [Fact]
        public void Open_Valve_should_not_pull_elements_again_when_closed_and_re_opened()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(new Valve<int>(), Keep.Both)
                .ToMaterialized(Sink.First<int>(), (l, r) => (l.Item1, l.Item2, r))
                .Run(Sys.Materializer());

            var probe = t.Item1;
            var switchTask = t.Item2;
            var resultTask = t.Item3;

            var valveSwitch = switchTask.AwaitResult();

            async Task<int> result()
            {
                await valveSwitch.Flip(SwitchMode.Close);
                await valveSwitch.Flip(SwitchMode.Open);
                probe.SendNext(1);
                probe.SendComplete();

                return await resultTask;
            }

            result().AwaitResult().Should().Be(1);
        }

        [Fact]
        public void Open_Valve_should_be_in_open_state()
        {
            var t = Source.From(Enumerable.Range(1, 5))
                .ViaMaterialized(new Valve<int>(), Keep.Right)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var switchTask = t.Item1;

            var valveSwitch = switchTask.AwaitResult();
            valveSwitch.GetMode().AwaitResult().Should().Be(SwitchMode.Open);
        }
    }
}
