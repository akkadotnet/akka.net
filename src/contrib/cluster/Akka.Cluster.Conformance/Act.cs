//-----------------------------------------------------------------------
// <copyright file="Act.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Cluster.Conformance
{
    /// <summary>
    /// Thrown by <see cref="ConformanceResult.EnsurePassed"/> when a node-under-test fails a
    /// conformance step. The message is the language-agnostic teaching text for the failed step.
    /// </summary>
    public sealed class ConformanceException : Exception
    {
        public ConformanceException(string message) : base(message) { }
    }

    /// <summary>
    /// The outcome of a conformance run. The checker is a "stop and teach" tool: it evaluates the
    /// lifecycle obligations in order and stops at the first one a node-under-test has not met,
    /// reporting a protocol-level (not language-specific) explanation of what the node must do.
    /// </summary>
    public sealed class ConformanceResult
    {
        public ConformanceResult(
            bool passed,
            int stepsCleared,
            int totalSteps,
            string? failedStep,
            string message,
            IReadOnlyList<string> clearedStepNames,
            ConformanceTrace trace)
        {
            Passed = passed;
            StepsCleared = stepsCleared;
            TotalSteps = totalSteps;
            FailedStep = failedStep;
            Message = message;
            ClearedStepNames = clearedStepNames;
            Trace = trace;
        }

        /// <summary>True only if every conformance step was satisfied.</summary>
        public bool Passed { get; }

        /// <summary>How many steps the node-under-test cleared before the run stopped.</summary>
        public int StepsCleared { get; }

        /// <summary>Total number of conformance steps.</summary>
        public int TotalSteps { get; }

        /// <summary>The name of the first failed step, or <c>null</c> if all passed.</summary>
        public string? FailedStep { get; }

        /// <summary>
        /// On failure, the teaching message explaining — in protocol terms — what the node-under-test
        /// must do to pass the failed step. On success, a short confirmation.
        /// </summary>
        public string Message { get; }

        /// <summary>Names of the steps that were satisfied.</summary>
        public IReadOnlyList<string> ClearedStepNames { get; }

        /// <summary>The full trace the verdict was derived from.</summary>
        public ConformanceTrace Trace { get; }

        /// <summary>Throws <see cref="ConformanceException"/> with the teaching message if not passed.</summary>
        public void EnsurePassed()
        {
            if (!Passed)
                throw new ConformanceException(Message);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            var header = Passed
                ? $"CONFORMANCE PASSED — all {TotalSteps} steps satisfied."
                : $"CONFORMANCE FAILED at step {StepsCleared + 1} of {TotalSteps}: {FailedStep}";

            var cleared = ClearedStepNames.Count == 0
                ? "  (none)"
                : string.Join(Environment.NewLine, ClearedStepNames.Select((s, i) => $"  [{i + 1}] OK   {s}"));

            return header
                   + Environment.NewLine + "Steps cleared:" + Environment.NewLine + cleared
                   + Environment.NewLine + Environment.NewLine + Message;
        }
    }

    /// <summary>
    /// <b>ACT — the Akka Conformance Tester.</b> Validates that a node-under-test (worker) correctly
    /// performs the Akka cluster membership lifecycle — connect, converge, gracefully leave, cleanly
    /// shut down — purely from what the instrumented <see cref="ReferenceSeed"/> observed.
    /// <para>
    /// This is a teaching tool. It checks one obligation at a time, in protocol order, and stops at
    /// the first unmet one with an explanation that describes the required messages and state
    /// transitions — deliberately without reference to any programming language or API — so that an
    /// implementer in any language can understand what their node must do next.
    /// </para>
    /// </summary>
    public static class Act
    {
        private sealed class Step
        {
            public Step(string name, Func<Context, bool> satisfied, Func<Context, string> teach)
            {
                Name = name;
                Satisfied = satisfied;
                Teach = teach;
            }

            public string Name { get; }
            public Func<Context, bool> Satisfied { get; }
            public Func<Context, string> Teach { get; }
        }

        private sealed class Context
        {
            public Context(ConformanceTrace trace, Address worker)
            {
                Trace = trace;
                Worker = worker;
            }

            public ConformanceTrace Trace { get; }
            public Address Worker { get; }

            public bool Protocol(string kind) => Trace.Has(kind, Worker, ConformanceSource.Protocol);
            public bool Membership(string kind) => Trace.Has(kind, Worker, ConformanceSource.Membership);

            public bool RemovedFromExiting() => Trace.Snapshot().Any(e =>
                e.Source == ConformanceSource.Membership
                && e.Kind == nameof(ClusterEvent.MemberRemoved)
                && Equals(e.Peer, Worker)
                && e.Detail.Contains("previousStatus=Exiting", StringComparison.Ordinal));
        }

        // The conformance ladder. Each rung is one observable obligation, in the order a node must
        // satisfy it. Teaching text is intentionally protocol-level and language-neutral.
        private static readonly IReadOnlyList<Step> Steps = new List<Step>
        {
            new("Initial contact (InitJoin / InitJoinAck)",
                ctx => ctx.Protocol("InitJoin") && ctx.Protocol("InitJoinAck"),
                ctx =>
                    "The node under test never completed initial contact with the seed.\n\n" +
                    "WHAT IS REQUIRED:\n" +
                    "On startup, a joining node must contact each of its configured seed nodes by sending an " +
                    "'InitJoin' message and then wait for the seed to reply with 'InitJoinAck'. This is the " +
                    "handshake that confirms the seed is alive and part of a cluster before the node attempts to join.\n\n" +
                    $"WHAT WAS OBSERVED:\n  InitJoin received from {ctx.Worker}: {(ctx.Protocol("InitJoin") ? "yes" : "no")}\n" +
                    $"  InitJoinAck sent to {ctx.Worker}: {(ctx.Protocol("InitJoinAck") ? "yes" : "no")}\n\n" +
                    "TO PASS:\n" +
                    "Make the node, at startup, repeatedly send an InitJoin to its seed node(s) until it receives " +
                    "an InitJoinAck. Ensure it is actually connecting to the seed's address and using the same " +
                    "transport protocol and cluster (actor system) name as the seed."),

            new("Join request (Join)",
                ctx => ctx.Protocol("Join"),
                ctx =>
                    "The node under test contacted the seed but never asked to join it.\n\n" +
                    "WHAT IS REQUIRED:\n" +
                    "After receiving InitJoinAck, the node must send a 'Join' message to the seed. The Join must " +
                    "carry the node's own unique address (host, port, and a unique incarnation identifier that " +
                    "distinguishes a restart from the previous instance), the set of roles the node advertises, and " +
                    "its application version.\n\n" +
                    "WHAT WAS OBSERVED:\n  The seed acknowledged initial contact but received no Join from " +
                    $"{ctx.Worker}.\n\n" +
                    "TO PASS:\n" +
                    "After the InitJoinAck handshake, send a Join message addressed to the seed's cluster core, " +
                    "populated with this node's unique address, roles, and application version."),

            new("Join accepted (Welcome)",
                ctx => ctx.Protocol("Welcome"),
                ctx =>
                    "The seed received a Join from the node under test but did not accept it (no Welcome was sent).\n\n" +
                    "WHAT IS REQUIRED:\n" +
                    "When a Join is valid, the seed responds with a 'Welcome' message that carries the current " +
                    "cluster gossip — now including the joining node with status 'Joining'. The node must accept the " +
                    "Welcome and adopt that gossip as its own starting view of the cluster.\n\n" +
                    "WHAT WAS OBSERVED:\n  A Join was received but no Welcome was sent back to " +
                    $"{ctx.Worker}. The seed only refuses to welcome a Join when the Join is malformed.\n\n" +
                    "TO PASS:\n" +
                    "Ensure the Join advertises a unique address whose host/port and transport protocol match how " +
                    "the node is actually reachable, uses the exact same cluster (actor system) name as the seed, and " +
                    "includes a well-formed roles set and application version. Then accept the returned Welcome and " +
                    "adopt its gossip state."),

            new("Gossip participation",
                ctx => ctx.Protocol("Gossip"),
                ctx =>
                    "The node under test was welcomed but never took part in the gossip protocol.\n\n" +
                    "WHAT IS REQUIRED:\n" +
                    "Cluster state is disseminated by gossip. After being welcomed, the node must periodically send " +
                    "its current view of the cluster — a gossip message containing the member set and the version " +
                    "information (a vector clock plus the 'seen' set) — to other members, and must merge any gossip " +
                    "it receives, reconciling differing versions with the vector clock.\n\n" +
                    "WHAT WAS OBSERVED:\n  No gossip messages were ever received from " +
                    $"{ctx.Worker}.\n\n" +
                    "TO PASS:\n" +
                    "Run a recurring gossip task: on each tick, pick another member and send it the node's current " +
                    "gossip; on receiving gossip, merge it, and update the version/seen information accordingly."),

            new("Convergence to Up (MemberUp)",
                ctx => ctx.Membership(nameof(ClusterEvent.MemberUp)),
                ctx =>
                    "The node under test joined and gossiped, but the cluster never converged enough to promote it " +
                    "to the 'Up' state.\n\n" +
                    "WHAT IS REQUIRED:\n" +
                    "A Joining node is promoted to Up by the cluster leader only after gossip 'converges' — that is, " +
                    "after every currently reachable member has seen the same gossip version. A node signals that it " +
                    "has seen a version by adding its own address to that gossip's 'seen' set before forwarding it.\n\n" +
                    "WHAT WAS OBSERVED:\n  Gossip was received from the node, but it never reached Up, which means " +
                    "convergence was never achieved.\n\n" +
                    "TO PASS:\n" +
                    "Every time the node receives or updates gossip, it must record itself in the 'seen' set for the " +
                    "current version and keep gossiping until all reachable members have seen it. Do not reset or drop " +
                    "the seen set, and do not keep producing new versions that never settle — otherwise the leader can " +
                    "never observe convergence and will never move the node to Up."),

            new("Graceful leave announced (Leaving)",
                ctx => ctx.Membership(nameof(ClusterEvent.MemberLeft)),
                ctx =>
                {
                    var wentUnreachable = ctx.Membership(nameof(ClusterEvent.UnreachableMember));
                    return
                        "The node under test did not leave the cluster gracefully.\n\n" +
                        "WHAT IS REQUIRED:\n" +
                        "To leave gracefully, a node must first set its own membership status to 'Leaving' and " +
                        "propagate that through gossip, so the rest of the cluster learns it intends to depart. It " +
                        "must NOT simply disconnect or stop responding.\n\n" +
                        "WHAT WAS OBSERVED:\n" +
                        (wentUnreachable
                            ? $"  {ctx.Worker} was seen to become UNREACHABLE — it stopped responding to heartbeats " +
                              "before announcing that it was leaving. From the cluster's point of view this is " +
                              "indistinguishable from a crash, not a graceful leave.\n\n"
                            : $"  {ctx.Worker} never entered the Leaving state.\n\n") +
                        "TO PASS:\n" +
                        "When shutting down, the node must initiate leaving first: mark itself Leaving, keep gossiping " +
                        "so that change spreads, and only continue the shutdown once the rest of the lifecycle " +
                        "(Exiting, then removal) has completed. It must keep answering heartbeats throughout.";
                }),

            new("Exiting reached (MemberExited)",
                ctx => ctx.Membership(nameof(ClusterEvent.MemberExited)),
                ctx =>
                    "The node under test announced it was Leaving, but never progressed to the 'Exiting' state.\n\n" +
                    "WHAT IS REQUIRED:\n" +
                    "Once a node's 'Leaving' status has converged across the cluster, the leader moves it to " +
                    "'Exiting'. The node must remain an active gossip participant through the Leaving → Exiting " +
                    "transition; it must not terminate its transport or stop gossiping while it is still Leaving.\n\n" +
                    "WHAT WAS OBSERVED:\n  The node entered Leaving but the Exiting transition was never seen for " +
                    $"{ctx.Worker} — it most likely stopped participating before its Leaving status converged.\n\n" +
                    "TO PASS:\n" +
                    "Keep the node alive and gossiping after it announces Leaving, until it observes itself moved to " +
                    "Exiting. Only then begin tearing anything down."),

            new("Exit confirmed (ExitingConfirmed)",
                ctx => ctx.Protocol("ExitingConfirmed"),
                ctx =>
                    "The node under test reached Exiting but never confirmed completion of its exit to the leader.\n\n" +
                    "WHAT IS REQUIRED:\n" +
                    "When a node has finished exiting, it must send an 'ExitingConfirmed' message to the leader before " +
                    "it shuts down. This explicit confirmation is what lets the leader remove the node safely and is " +
                    "precisely what distinguishes a clean, intentional shutdown from a crash.\n\n" +
                    "WHAT WAS OBSERVED:\n  The node reached Exiting, but no ExitingConfirmed was received from " +
                    $"{ctx.Worker}.\n\n" +
                    "TO PASS:\n" +
                    "Upon observing itself in the Exiting state, the node must send ExitingConfirmed to the current " +
                    "leader, and only then complete its shutdown."),

            new("Clean removal (MemberRemoved from Exiting, never Downed)",
                ctx => ctx.Membership(nameof(ClusterEvent.MemberRemoved))
                       && ctx.RemovedFromExiting()
                       && !ctx.Membership(nameof(ClusterEvent.MemberDowned)),
                ctx =>
                {
                    var removed = ctx.Membership(nameof(ClusterEvent.MemberRemoved));
                    var downed = ctx.Membership(nameof(ClusterEvent.MemberDowned));
                    string observed;
                    if (downed)
                        observed = $"  {ctx.Worker} was DOWNED (forcibly removed after being considered failed) " +
                                   "rather than removed cleanly from the Exiting state.";
                    else if (removed && !ctx.RemovedFromExiting())
                        observed = $"  {ctx.Worker} was removed, but not from the Exiting state — a clean leave must " +
                                   "end with removal whose previous status is Exiting.";
                    else
                        observed = $"  {ctx.Worker} was never removed from the membership at all.";

                    return
                        "The node under test did not achieve a clean removal.\n\n" +
                        "WHAT IS REQUIRED:\n" +
                        "A cleanly shutting-down node must end as 'Removed' with its previous status being 'Exiting', " +
                        "and it must never have been marked Unreachable or Downed along the way. Being Downed means the " +
                        "cluster treated the node as a failure rather than as an intentional departure.\n\n" +
                        "WHAT WAS OBSERVED:\n" + observed + "\n\n" +
                        "TO PASS:\n" +
                        "Complete the full graceful sequence — Leaving, then Exiting, then ExitingConfirmed — and only " +
                        "stop the node after the leader has removed it from Exiting. Never let the node go silent " +
                        "(stop answering heartbeats) before that sequence finishes, or it will be Downed instead.";
                })
        };

        /// <summary>
        /// Evaluates the conformance ladder against <paramref name="trace"/> for the given
        /// <paramref name="worker"/> address, stopping at the first unmet obligation.
        /// </summary>
        public static ConformanceResult Check(ConformanceTrace trace, Address worker)
        {
            var ctx = new Context(trace, worker);
            var cleared = new List<string>();

            foreach (var step in Steps)
            {
                if (step.Satisfied(ctx))
                {
                    cleared.Add(step.Name);
                    continue;
                }

                var message =
                    $"Conformance step {cleared.Count + 1} of {Steps.Count} — {step.Name}\n" +
                    "========================================================================\n" +
                    step.Teach(ctx);

                return new ConformanceResult(
                    passed: false,
                    stepsCleared: cleared.Count,
                    totalSteps: Steps.Count,
                    failedStep: step.Name,
                    message: message,
                    clearedStepNames: cleared,
                    trace: trace);
            }

            return new ConformanceResult(
                passed: true,
                stepsCleared: Steps.Count,
                totalSteps: Steps.Count,
                failedStep: null,
                message: $"The node under test correctly connected, converged, gracefully left, and cleanly shut down " +
                         $"(all {Steps.Count} conformance steps satisfied).",
                clearedStepNames: cleared,
                trace: trace);
        }
    }
}
