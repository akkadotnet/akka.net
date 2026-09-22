//-----------------------------------------------------------------------
// <copyright file="SharedTestStateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Remote.Transport;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Pure state tests for <see cref="SharedTestState"/>, the blackhole map behind artery's
    /// <c>advanced.test-mode</c> failure injection. No actor system required. The
    /// direction/key-order assertions here are the load-bearing ones: BOTH test stages check
    /// <c>(localAddress, remoteAddress)</c>, so these directed entries are exactly what
    /// determines which node drops what.
    /// </summary>
    public class SharedTestStateSpec
    {
        private static readonly Address A = new("akka", "sysA", "10.0.0.1", 2551);
        private static readonly Address B = new("akka", "sysB", "10.0.0.2", 2552);
        private static readonly Address C = new("akka", "sysC", "10.0.0.3", 2553);

        [Fact(DisplayName = "Blackhole(a, b, Send) adds exactly the directed pair (a -> b)")]
        public void Should_Add_Directed_Pair_For_Send()
        {
            var state = new SharedTestState();
            state.Blackhole(A, B, ThrottleTransportAdapter.Direction.Send);

            state.IsBlackhole(A, B).Should().BeTrue();
            state.IsBlackhole(B, A).Should().BeFalse("Send must not add the reverse pair");
            state.AnyBlackholePresent().Should().BeTrue();
        }

        [Fact(DisplayName = "Blackhole(a, b, Receive) adds exactly the reversed pair (b -> a)")]
        public void Should_Add_Reversed_Pair_For_Receive()
        {
            var state = new SharedTestState();
            state.Blackhole(A, B, ThrottleTransportAdapter.Direction.Receive);

            state.IsBlackhole(B, A).Should().BeTrue();
            state.IsBlackhole(A, B).Should().BeFalse(
                "Receive adds only (b -> a) -- neither of node a's own (a, b)-keyed stage checks matches it");
        }

        [Fact(DisplayName = "Blackhole(a, b, Both) adds both directed pairs")]
        public void Should_Add_Both_Pairs_For_Both()
        {
            var state = new SharedTestState();
            state.Blackhole(A, B, ThrottleTransportAdapter.Direction.Both);

            state.IsBlackhole(A, B).Should().BeTrue();
            state.IsBlackhole(B, A).Should().BeTrue();
        }

        [Fact(DisplayName = "PassThrough removes only the intended directed pair, leaving unrelated pairs intact")]
        public void Should_Remove_Only_Intended_Pair()
        {
            var state = new SharedTestState();
            state.Blackhole(A, B, ThrottleTransportAdapter.Direction.Both);
            state.Blackhole(A, C, ThrottleTransportAdapter.Direction.Send);

            state.PassThrough(A, B, ThrottleTransportAdapter.Direction.Send); // removes (a -> b) only

            state.IsBlackhole(A, B).Should().BeFalse();
            state.IsBlackhole(B, A).Should().BeTrue("only the (a -> b) direction was healed");
            state.IsBlackhole(A, C).Should().BeTrue("an unrelated pair must never be affected");
        }

        [Fact(DisplayName = "PassThrough(Both) heals both directions")]
        public void Should_Heal_Both_Directions()
        {
            var state = new SharedTestState();
            state.Blackhole(A, B, ThrottleTransportAdapter.Direction.Both);
            state.PassThrough(A, B, ThrottleTransportAdapter.Direction.Both);

            state.IsBlackhole(A, B).Should().BeFalse();
            state.IsBlackhole(B, A).Should().BeFalse();
        }

        [Fact(DisplayName = "AnyBlackholePresent returns false again after a full PassThrough heal (no stale residue)")]
        public void Should_Clear_AnyBlackholePresent_After_Full_Heal()
        {
            var state = new SharedTestState();
            state.AnyBlackholePresent().Should().BeFalse("pristine state has no entries");

            state.Blackhole(A, B, ThrottleTransportAdapter.Direction.Both);
            state.AnyBlackholePresent().Should().BeTrue();

            state.PassThrough(A, B, ThrottleTransportAdapter.Direction.Both);

            state.IsBlackhole(A, B).Should().BeFalse();
            state.IsBlackhole(B, A).Should().BeFalse();
            state.AnyBlackholePresent().Should().BeFalse(
                "healing the only active pair must drop the now-empty key entirely, otherwise the " +
                "unknown-origin inbound gate would stay on forever even though nothing is blackholed");
        }

        [Fact(DisplayName = "AnyBlackholePresent stays true while an unrelated pair is still blackholed")]
        public void Should_Keep_AnyBlackholePresent_While_Other_Pair_Active()
        {
            var state = new SharedTestState();
            state.Blackhole(A, B, ThrottleTransportAdapter.Direction.Both);
            state.Blackhole(A, C, ThrottleTransportAdapter.Direction.Send);

            state.PassThrough(A, B, ThrottleTransportAdapter.Direction.Both);

            state.IsBlackhole(A, B).Should().BeFalse();
            state.AnyBlackholePresent().Should().BeTrue("the unrelated (a -> c) pair is still active");
        }

        [Fact(DisplayName = "PassThrough on a pristine state is a harmless no-op")]
        public void Should_Tolerate_Heal_Without_Prior_Blackhole()
        {
            var state = new SharedTestState();
            state.PassThrough(A, B, ThrottleTransportAdapter.Direction.Both);

            state.IsBlackhole(A, B).Should().BeFalse();
            state.AnyBlackholePresent().Should().BeFalse("no key was ever added, so none is present");
        }

        [Fact(DisplayName = "concurrent adds from many threads all converge (CAS-loop correctness)")]
        public void Should_Converge_Under_Concurrent_Mutation()
        {
            var state = new SharedTestState();
            const int pairs = 200;

            Parallel.For(0, pairs, i =>
            {
                var from = new Address("akka", "sys", $"10.1.{i / 250}.{i % 250}", 2551);
                state.Blackhole(from, B, ThrottleTransportAdapter.Direction.Both);
            });

            for (var i = 0; i < pairs; i++)
            {
                var from = new Address("akka", "sys", $"10.1.{i / 250}.{i % 250}", 2551);
                state.IsBlackhole(from, B).Should().BeTrue($"pair {i} (forward) must have survived the concurrent CAS updates");
                state.IsBlackhole(B, from).Should().BeTrue($"pair {i} (reverse) must have survived the concurrent CAS updates");
            }
        }
    }
}
