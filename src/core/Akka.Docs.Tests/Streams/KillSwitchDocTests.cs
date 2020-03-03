//-----------------------------------------------------------------------
// <copyright file="KillSwitchDocTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DocsExamples.Streams
{
    public class KillSwitchDocTests : TestKit
    {
        private ActorMaterializer Materializer { get; }

        public KillSwitchDocTests(ITestOutputHelper output) 
            : base("{}", output)
        {
            Materializer = Sys.Materializer();
        }

        private void DoSomethingElse()
        {
        }

        [Fact]
        public void Unique_kill_switch_must_control_graph_completion_with_shutdown()
        {
            #region unique-shutdown
            var countingSrc = Source.From(Enumerable.Range(1, int.MaxValue)).Delay(1.Seconds(), DelayOverflowStrategy.Backpressure);
            var lastSink = Sink.Last<int>();

            var (killSwitch, last) = countingSrc
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .ToMaterialized(lastSink, Keep.Both)
                .Run(Materializer);

            DoSomethingElse();

            killSwitch.Shutdown();

            AwaitCondition(() => last.IsCompleted);
            #endregion
        }

        [Fact]
        public void Unique_kill_switch_must_control_graph_completion_with_abort()
        {
            #region unique-abort
            var countingSrc = Source.From(Enumerable.Range(1, int.MaxValue)).Delay(1.Seconds(), DelayOverflowStrategy.Backpressure);
            var lastSink = Sink.Last<int>();

            var (killSwitch, last) = countingSrc
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .ToMaterialized(lastSink, Keep.Both)
                .Run(Materializer);

            var error = new Exception("boom");
            killSwitch.Abort(error);

            last.ContinueWith(t => { /* Ignore exception */ }).Wait(1.Seconds());
            last.Exception.GetBaseException().Should().Be(error);
            #endregion
        }

        [Fact]
        public void Shared_kill_switch_must_control_graph_completion_with_shutdown()
        {
            #region shared-shutdown
            var countingSrc = Source.From(Enumerable.Range(1, int.MaxValue)).Delay(1.Seconds(), DelayOverflowStrategy.Backpressure);
            var lastSink = Sink.Last<int>();
            var sharedKillSwitch = KillSwitches.Shared("my-kill-switch");

            var last = countingSrc
                .Via(sharedKillSwitch.Flow<int>())
                .RunWith(lastSink, Materializer);

            var delayedLast = countingSrc
                .Delay(1.Seconds(), DelayOverflowStrategy.Backpressure)
                .Via(sharedKillSwitch.Flow<int>())
                .RunWith(lastSink, Materializer);

            DoSomethingElse();

            sharedKillSwitch.Shutdown();

            AwaitCondition(() => last.IsCompleted);
            AwaitCondition(() => delayedLast.IsCompleted);
            #endregion
        }

        [Fact]
        public void Shared_kill_switch_must_control_graph_completion_with_abort()
        {
            #region shared-abort
            var countingSrc = Source.From(Enumerable.Range(1, int.MaxValue)).Delay(1.Seconds());
            var lastSink = Sink.Last<int>();
            var sharedKillSwitch = KillSwitches.Shared("my-kill-switch");

            var last1 = countingSrc.Via(sharedKillSwitch.Flow<int>()).RunWith(lastSink, Materializer);
            var last2 = countingSrc.Via(sharedKillSwitch.Flow<int>()).RunWith(lastSink, Materializer);

            var error = new Exception("boom");
            sharedKillSwitch.Abort(error);

            last1.ContinueWith(t => { /* Ignore exception */ }).Wait(1.Seconds());
            last1.Exception.GetBaseException().Should().Be(error);

            last2.ContinueWith(t => { /* Ignore exception */ }).Wait(1.Seconds());
            last2.Exception.GetBaseException().Should().Be(error);
            #endregion
        }
    }
}
