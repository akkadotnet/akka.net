//-----------------------------------------------------------------------
// <copyright file="ReferenceSeed.cs" company="Akka.NET Project">
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
    /// An instrumented, single-node "reference" cluster that a node-under-test (worker) joins. The
    /// reference node enables <c>akka.cluster.protocol-recorder</c> and runs a
    /// <see cref="ConformanceRecorderActor"/> that captures the full protocol exchange and membership
    /// transitions into a <see cref="Trace"/>. The worker that joins requires no instrumentation
    /// whatsoever — everything is observed from the reference side.
    /// </summary>
    public sealed class ReferenceSeed : IAsyncDisposable
    {
        private ReferenceSeed(ActorSystem system, Cluster cluster, ConformanceTrace trace, string seedNodeUri)
        {
            System = system;
            Cluster = cluster;
            Trace = trace;
            SeedNodeUri = seedNodeUri;
        }

        /// <summary>The reference node's actor system.</summary>
        public ActorSystem System { get; }

        /// <summary>The reference node's <see cref="Cluster"/> extension.</summary>
        public Cluster Cluster { get; }

        /// <summary>The address of the reference node.</summary>
        public Address Address => Cluster.SelfAddress;

        /// <summary>The ordered, growing trace of everything the reference node has observed.</summary>
        public ConformanceTrace Trace { get; }

        /// <summary>The <c>akka.tcp://...</c> URI a worker should use as its seed node to join.</summary>
        public string SeedNodeUri { get; }

        /// <summary>
        /// Starts a reference seed, waits until it has formed the cluster (its own member is Up), and
        /// begins recording. The returned instance is ready for a worker to join.
        /// </summary>
        /// <param name="systemName">Cluster (actor system) name; the worker must use the same name.</param>
        /// <param name="host">Bind host, e.g. <c>127.0.0.1</c>.</param>
        /// <param name="port">Bind port for the reference node.</param>
        /// <param name="formTimeout">How long to wait for the reference node to reach Up.</param>
        /// <param name="extraConfig">Optional extra HOCON merged (as a fallback) into the seed config.</param>
        public static async Task<ReferenceSeed> StartAsync(
            string systemName,
            string host = "127.0.0.1",
            int port = 0,
            TimeSpan? formTimeout = null,
            Config? extraConfig = null)
        {
            // No seed-nodes: we bind an ephemeral port (port 0) and bootstrap the cluster manually via
            // Cluster.Join(self) below, using the *actual* bound address. This avoids fixed ports (so
            // tests never collide) and the port-0-in-seed-list bootstrap problem.
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
                        protocol-recorder = on
                        roles = [""reference-seed""]
                        # keep an unreachable worker from lingering forever in negative tests
                        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider, Akka.Cluster""
                    }}
                }}");

            if (extraConfig is not null)
                config = config.WithFallback(extraConfig);

            var system = ActorSystem.Create(systemName, config);
            var cluster = Cluster.Get(system);

            var trace = new ConformanceTrace();
            system.ActorOf(ConformanceRecorderActor.Props(trace), "conformance-recorder");

            // Bootstrap as a single-node cluster on the actual bound address.
            cluster.Join(cluster.SelfAddress);

            // Resolve the actual bound address (in case port 0 was used) for the worker to join.
            var selfUri = cluster.SelfAddress.ToString();

            var seed = new ReferenceSeed(system, cluster, trace, selfUri);

            var ok = await seed.WaitForMemberStatusAsync(cluster.SelfAddress, MemberStatus.Up,
                formTimeout ?? TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            if (!ok)
            {
                await seed.DisposeAsync().ConfigureAwait(false);
                throw new TimeoutException("Reference seed did not reach 'Up' within the allotted time.");
            }

            return seed;
        }

        /// <summary>Waits until <paramref name="member"/> is observed at exactly <paramref name="status"/>.</summary>
        public Task<bool> WaitForMemberStatusAsync(Address member, MemberStatus status, TimeSpan timeout) =>
            WaitAsync(() => Cluster.State.Members.Any(m => m.Address.Equals(member) && m.Status == status), timeout);

        /// <summary>Waits until <paramref name="member"/> is no longer present in the membership (fully Removed).</summary>
        public Task<bool> WaitForRemovedAsync(Address member, TimeSpan timeout) =>
            WaitAsync(() => Cluster.State.Members.All(m => !m.Address.Equals(member)), timeout);

        /// <summary>Waits until exactly <paramref name="count"/> members are <c>Up</c>.</summary>
        public Task<bool> WaitForUpMembersAsync(int count, TimeSpan timeout) =>
            WaitAsync(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up) == count, timeout);

        private static async Task<bool> WaitAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (condition())
                        return true;
                }
                catch
                {
                    // membership view not ready yet; keep polling
                }

                await Task.Delay(200).ConfigureAwait(false);
            }

            return false;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await System.Terminate().ConfigureAwait(false);
        }
    }
}
