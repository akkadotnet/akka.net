//-----------------------------------------------------------------------
// <copyright file="WorkerUnderTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Conformance
{
    /// <summary>
    /// A node-under-test ("worker"). The in-process implementation is a completely stock Akka.Cluster
    /// node — it carries no instrumentation, no special config and no awareness that it is being
    /// tested. Everything about its conformance is judged from the <see cref="ReferenceSeed"/> side.
    /// The same harness can drive an external, black-box worker; only the lifecycle triggers differ.
    /// </summary>
    public sealed class InProcessWorker : IAsyncDisposable
    {
        private readonly bool _simulateCrashOnStop;
        private bool _stopped;

        private InProcessWorker(ActorSystem system, Cluster cluster, bool simulateCrashOnStop)
        {
            System = system;
            Cluster = cluster;
            _simulateCrashOnStop = simulateCrashOnStop;
        }

        /// <summary>The worker's actor system.</summary>
        public ActorSystem System { get; }

        /// <summary>The worker's <see cref="Cluster"/> extension.</summary>
        public Cluster Cluster { get; }

        /// <summary>The worker's address.</summary>
        public Address Address => Cluster.SelfAddress;

        /// <summary>
        /// Starts a stock worker that will join <paramref name="seedNodeUri"/>.
        /// </summary>
        /// <param name="systemName">Must match the reference seed's cluster (actor system) name.</param>
        /// <param name="seedNodeUri">The reference seed URI to join.</param>
        /// <param name="host">Bind host.</param>
        /// <param name="port">Bind port (0 = auto-assign).</param>
        /// <param name="role">Cluster role to advertise.</param>
        /// <param name="simulateCrashOnStop">
        /// When <c>true</c>, the worker is configured so that terminating it does <b>not</b> run
        /// coordinated shutdown — i.e. it vanishes without leaving the cluster gracefully. Used by the
        /// negative conformance test to prove the harness distinguishes a crash from a clean leave.
        /// </param>
        public static InProcessWorker Start(
            string systemName,
            string seedNodeUri,
            string host = "127.0.0.1",
            int port = 0,
            string role = "worker",
            bool simulateCrashOnStop = false)
        {
            // Deliberately minimal, representative-of-stock configuration. No recorder, nothing special.
            // NOTE: this fragment is spliced inside the 'akka { }' block below, so it must be written
            // relative to 'akka' (no leading 'akka.').
            var crashConfig = simulateCrashOnStop
                ? "coordinated-shutdown.run-by-actor-system-terminate = off"
                : "";

            var config = ConfigurationFactory.ParseString($@"
                akka {{
                    actor.provider = cluster
                    loglevel = INFO
                    stdout-loglevel = INFO
                    log-dead-letters = off
                    log-dead-letters-during-shutdown = off
                    remote.dot-netty.tcp {{
                        hostname = ""{host}""
                        port = {port}
                        log-transport = off
                    }}
                    cluster {{
                        seed-nodes = [""{seedNodeUri}""]
                        roles = [""{role}""]
                    }}
                    {crashConfig}
                }}");

            var system = ActorSystem.Create(systemName, config);
            return new InProcessWorker(system, Cluster.Get(system), simulateCrashOnStop);
        }

        /// <summary>Waits until this worker considers itself <c>Up</c> in the cluster.</summary>
        public async Task<bool> WaitUntilUpAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Cluster.State.Members.Any(m => m.Address.Equals(Address) && m.Status == MemberStatus.Up))
                    return true;
                await Task.Delay(200).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>Requests a graceful leave (Leaving → Exiting → Removed) of this worker.</summary>
        public void LeaveGracefully() => Cluster.Leave(Address);

        /// <summary>
        /// Abruptly terminates the worker. If it was started with <c>simulateCrashOnStop</c> this skips
        /// coordinated shutdown, so the reference node observes it as unreachable rather than as a clean leave.
        /// </summary>
        public async Task CrashAsync()
        {
            _stopped = true;
            await System.Terminate().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_stopped)
                return;
            _stopped = true;

            // A normal worker shutdown runs coordinated shutdown, which gracefully leaves the cluster.
            await System.Terminate().ConfigureAwait(false);
        }
    }
}
