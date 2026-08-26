//-----------------------------------------------------------------------
// <copyright file="GossipTombstoneGenerators.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Util;
using CsCheck;
using static Akka.Cluster.ClusterCoreDaemon;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// Generators and equivalence helpers shared by the gossip removal-tombstone property specs.
    ///
    /// Everything under test is pure: <see cref="Gossip"/> is immutable, so a property only has to
    /// build inputs and compare outputs. All randomness comes from CsCheck generators and every
    /// timestamp is passed in, so a failing run replays from its printed seed.
    /// </summary>
    internal static class GossipTombstoneGenerators
    {
        /// <summary>
        /// The bounded universe the properties draw from. Six nodes is enough to get interesting splits
        /// while keeping the search space small enough that a few hundred iterations cover it.
        ///
        /// Index 2 and index 3 share a host and port and differ only by UID. That pair is the serializer
        /// trap: a serializer that resolves a tombstone through the member address table hands back the
        /// wrong UID for it, and the failure is silent.
        /// </summary>
        public static readonly ImmutableArray<UniqueAddress> Universe = ImmutableArray.Create(
            new UniqueAddress(new Address("akka.tcp", "sys", "a", 2552), 1),
            new UniqueAddress(new Address("akka.tcp", "sys", "b", 2552), 2),
            new UniqueAddress(new Address("akka.tcp", "sys", "c", 2552), 3),
            new UniqueAddress(new Address("akka.tcp", "sys", "c", 2552), 4),
            new UniqueAddress(new Address("akka.tcp", "sys", "d", 2552), 5),
            new UniqueAddress(new Address("akka.tcp", "sys", "e", 2552), 6));

        public static int NodeCount => Universe.Length;

        private static readonly ImmutableArray<ImmutableHashSet<string>> Roles = ImmutableArray.Create(
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet.Create("r1"),
            ImmutableHashSet.Create("r1", "r2"),
            ImmutableHashSet.Create("r2"),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet.Create("r3"));

        private static readonly ImmutableArray<AppVersion> AppVersions = ImmutableArray.Create(
            AppVersion.Zero,
            AppVersion.Create("1.0.0"),
            AppVersion.Create("1.1.0"),
            AppVersion.Create("1.1.0"),
            AppVersion.Create("2.0.0"),
            AppVersion.Zero);

        /// <summary>
        /// Vector clock nodes are MD5 hashes of the address, so they are computed once and reused.
        /// </summary>
        private static readonly ImmutableArray<VectorClock.Node> VclockNodes =
            Universe.Select(a => VectorClock.Node.Create(VclockName(a))).ToImmutableArray();

        /// <summary>Every status a live member may hold.</summary>
        private static readonly ImmutableArray<MemberStatus> AllStatuses = ImmutableArray.Create(
            MemberStatus.Joining, MemberStatus.WeaklyUp, MemberStatus.Up,
            MemberStatus.Leaving, MemberStatus.Exiting, MemberStatus.Down);

        /// <summary>
        /// The statuses the associativity property draws from.
        ///
        /// <see cref="Member.PickHighestPriority(IEnumerable{Member},IEnumerable{Member},IImmutableSet{UniqueAddress})"/>
        /// drops a one-sided <c>Down</c> or <c>Exiting</c> member, and that rule is not associative on its
        /// own: a node that is Up on one side and Down on another is Down after the first merge and then
        /// dropped by the second, but survives when the same three sides are grouped the other way. That
        /// predates tombstones. Leaving those two statuses out isolates the part associativity can speak
        /// about, and removals still get exercised - through tombstones, which is the point.
        /// </summary>
        private static readonly ImmutableArray<MemberStatus> NonTerminalStatuses = ImmutableArray.Create(
            MemberStatus.Joining, MemberStatus.WeaklyUp, MemberStatus.Up, MemberStatus.Leaving);

        public static UniqueAddress Node(int i) => Universe[i];

        public static VectorClock.Node VclockNodeOf(UniqueAddress address) =>
            VclockNodes[Universe.IndexOf(address)];

        public static Member MemberOf(int i, MemberStatus status) =>
            Member.Create(Universe[i], upNumber: i + 1, status, Roles[i], AppVersions[i]);

        /// <summary>How the sides of a merge may be shaped.</summary>
        public enum Shape
        {
            /// <summary>Every side obeys the Gossip invariant: its members and its tombstones are disjoint.</summary>
            Legal,

            /// <summary>
            /// A side may carry the same node as a member and as a tombstone. The Gossip constructor
            /// rejects that when <c>AKKA_CLUSTER_ASSERT=on</c>, but nothing else does, so the merge has to
            /// hold up against it.
            /// </summary>
            Adversarial,

            /// <summary>
            /// <see cref="Legal"/>, and no member ever holds a terminal status. See
            /// <see cref="NonTerminalStatuses"/> for why associativity needs this.
            /// </summary>
            NonTerminal
        }

        /// <summary>
        /// The raw draws behind one merge case. Kept flat so the whole case comes out of a single
        /// generator and shrinks as one unit.
        /// </summary>
        internal sealed class Draw
        {
            public Draw(int[] baseStatus, bool[] isMember, int[] statusStep, bool[] isTombstone,
                long[] timestamps, int[] clockTicks, bool[] seen, int[] reachOps)
            {
                BaseStatus = baseStatus;
                IsMember = isMember;
                StatusStep = statusStep;
                IsTombstone = isTombstone;
                Timestamps = timestamps;
                ClockTicks = clockTicks;
                Seen = seen;
                ReachOps = reachOps;
            }

            public int[] BaseStatus { get; }
            public bool[] IsMember { get; }
            public int[] StatusStep { get; }
            public bool[] IsTombstone { get; }
            public long[] Timestamps { get; }
            public int[] ClockTicks { get; }
            public bool[] Seen { get; }
            public int[] ReachOps { get; }
        }

        private const int ReachOpsPerSide = 4;

        /// <summary>
        /// Generates <paramref name="sides"/> gossips over the shared universe.
        ///
        /// Each node gets a base status; every side that holds it as a member holds it either at that
        /// status or at one legal transition away from it, so no pair of sides disagrees in a way the
        /// cluster could never produce. Membership is an independent draw per side, which is what
        /// produces the overlapping splits the merge has to reconcile.
        /// </summary>
        public static Gen<Gossip[]> Sides(int sides, Shape shape = Shape.Legal)
        {
            var n = NodeCount;
            var perSide = sides * n;

            var draw = Gen.Select(
                Gen.Int[0, 5].Array[n],
                Gen.Bool.Array[perSide],
                Gen.Int[0, 4].Array[perSide],
                Gen.Bool.Array[perSide],
                // a four value pool guarantees collisions, which is what makes the max-timestamp rule bite
                Gen.Long[1L, 4L].Array[perSide],
                Gen.Int[0, 2].Array[perSide],
                Gen.Bool.Array[perSide],
                Gen.Int[0, 999].Array[sides * ReachOpsPerSide],
                (a, b, c, d, e, f, g, h) => new Draw(a, b, c, d, e, f, g, h));

            return draw.Select(d => Build(d, sides, shape));
        }

        private static Gossip[] Build(Draw d, int sides, Shape shape)
        {
            var n = NodeCount;
            var statuses = shape == Shape.NonTerminal ? NonTerminalStatuses : AllStatuses;

            // per node: the base status, and per side: whether the side holds it and at which status
            var memberOn = new bool[sides][];
            var statusOn = new MemberStatus[sides][];
            for (var s = 0; s < sides; s++)
            {
                memberOn[s] = new bool[n];
                statusOn[s] = new MemberStatus[n];
            }

            for (var i = 0; i < n; i++)
            {
                var baseStatus = statuses[d.BaseStatus[i] % statuses.Length];
                for (var s = 0; s < sides; s++)
                {
                    var k = s * n + i;
                    memberOn[s][i] = d.IsMember[k];
                    statusOn[s][i] = StepFrom(baseStatus, d.StatusStep[k], shape);
                }
            }

            // tombstones: a side never tombstones a node it holds as a member, unless the shape says otherwise
            var tombstoneOn = new bool[sides][];
            for (var s = 0; s < sides; s++)
            {
                tombstoneOn[s] = new bool[n];
                for (var i = 0; i < n; i++)
                {
                    var k = s * n + i;
                    tombstoneOn[s][i] = d.IsTombstone[k]
                                        && (shape == Shape.Adversarial || !memberOn[s][i]);
                }
            }

            // Reachability observations are drawn from the nodes every side holds and nobody tombstones.
            //
            // Two reasons. Reachability.Merge breaks an equal-version tie by taking whichever side was
            // passed second, so the same observer at the same version on both sides is not commutative -
            // that predates tombstones, so each side gets its own observers here. And a record whose
            // subject is not a member is filtered out by the merge, which would make associativity depend
            // on the grouping. Neither has anything to do with removals.
            var core = new List<int>();
            for (var i = 0; i < n; i++)
            {
                var everywhere = true;
                for (var s = 0; s < sides && everywhere; s++)
                    everywhere = memberOn[s][i] && !tombstoneOn[s][i];
                if (everywhere) core.Add(i);
            }

            var result = new Gossip[sides];
            for (var s = 0; s < sides; s++)
            {
                var members = ImmutableSortedSet<Member>.Empty;
                for (var i = 0; i < n; i++)
                    if (memberOn[s][i])
                        members = members.Add(MemberOf(i, statusOn[s][i]));

                var tombstones = ImmutableDictionary<UniqueAddress, long>.Empty;
                for (var i = 0; i < n; i++)
                    if (tombstoneOn[s][i])
                        tombstones = tombstones.Add(Universe[i], d.Timestamps[s * n + i]);

                var version = VectorClock.Create();
                for (var i = 0; i < n; i++)
                    for (var t = 0; t < d.ClockTicks[s * n + i]; t++)
                        version = version.Increment(VclockNodes[i]);

                // observers are partitioned across the sides so no two sides observe from the same node
                var observers = core.Where(i => i % sides == s).ToList();
                var reachability = Reachability.Empty;
                if (observers.Count > 0 && core.Count > 1)
                {
                    for (var op = 0; op < ReachOpsPerSide; op++)
                    {
                        var v = d.ReachOps[s * ReachOpsPerSide + op];
                        var observer = Universe[observers[v / 100 % observers.Count]];
                        var subject = Universe[core[v / 10 % core.Count]];
                        if (observer.Equals(subject)) continue;
                        reachability = (v % 3) switch
                        {
                            0 => reachability.Unreachable(observer, subject),
                            1 => reachability.Reachable(observer, subject),
                            _ => reachability.Terminated(observer, subject)
                        };
                    }
                }

                var seen = ImmutableHashSet<UniqueAddress>.Empty;
                for (var i = 0; i < n; i++)
                    if (memberOn[s][i] && d.Seen[s * n + i])
                        seen = seen.Add(Universe[i]);

                result[s] = new Gossip(members, new GossipOverview(seen, reachability), version, tombstones);
            }

            return result;
        }

        /// <summary>
        /// Picks a status for one side of one node: either the node's base status, or one legal
        /// transition away from it. Sides never disagree by an illegal jump.
        /// </summary>
        private static MemberStatus StepFrom(MemberStatus baseStatus, int step, Shape shape)
        {
            if (step == 0) return baseStatus;

            var next = Member.AllowedTransitions[baseStatus]
                .Where(s => s != MemberStatus.Removed) // a live gossip never holds a Removed member
                .Where(s => shape != Shape.NonTerminal || NonTerminalStatuses.Contains(s))
                .OrderBy(s => (int)s)
                .ToArray();

            return next.Length == 0 ? baseStatus : next[(step - 1) % next.Length];
        }

        // -----------------------------------------------------------------------------------------
        // Equivalence
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The parts of a gossip two merge orders must agree on. Reachability records and merged member
        /// sets come out in whatever order the merge built them, so both are compared as sets.
        /// </summary>
        public static string Describe(Gossip g)
        {
            var members = g.Members
                .Select(m => $"{m.UniqueAddress}/{m.Status}/{m.UpNumber}/[{string.Join(",", m.Roles.OrderBy(r => r, StringComparer.Ordinal))}]/{m.AppVersion.Version}")
                .OrderBy(x => x, StringComparer.Ordinal);

            var tombstones = g.Tombstones
                .Select(t => $"{t.Key}={t.Value}")
                .OrderBy(x => x, StringComparer.Ordinal);

            var records = g.Overview.Reachability.Records
                .Select(r => $"{r.Observer}->{r.Subject}/{r.Status}/{r.Version}")
                .OrderBy(x => x, StringComparer.Ordinal);

            var reachVersions = g.Overview.Reachability.Versions
                .Select(v => $"{v.Key}={v.Value}")
                .OrderBy(x => x, StringComparer.Ordinal);

            var version = g.Version.Versions
                .Select(v => $"{v.Key}={v.Value}")
                .OrderBy(x => x, StringComparer.Ordinal);

            return $"members=[{string.Join("; ", members)}]\n" +
                   $"tombstones=[{string.Join("; ", tombstones)}]\n" +
                   $"reachability=[{string.Join("; ", records)}] versions=[{string.Join("; ", reachVersions)}]\n" +
                   $"clock=[{string.Join("; ", version)}]";
        }

        /// <summary>Members, tombstones and clock only - what the sequence properties converge on.</summary>
        public static string DescribeCore(Gossip g)
        {
            var members = g.Members.Select(m => $"{m.UniqueAddress}/{m.Status}")
                .OrderBy(x => x, StringComparer.Ordinal);
            var tombstones = g.Tombstones.Select(t => $"{t.Key}={t.Value}")
                .OrderBy(x => x, StringComparer.Ordinal);
            var version = g.Version.Versions.Select(v => $"{v.Key}={v.Value}")
                .OrderBy(x => x, StringComparer.Ordinal);

            return $"members=[{string.Join("; ", members)}] tombstones=[{string.Join("; ", tombstones)}] " +
                   $"clock=[{string.Join("; ", version)}]";
        }

        public static string Print(IEnumerable<Gossip> gossips) =>
            string.Join("\n----\n", gossips.Select((g, i) => $"side {i}:\n{Describe(g)}"));

        /// <summary>The clock entry names of every node this gossip has tombstoned.</summary>
        public static ImmutableHashSet<VectorClock.Node> TombstonedClockNodes(Gossip g) =>
            g.Tombstones.Keys.Select(VclockNodeOf).ToImmutableHashSet();
    }
}
