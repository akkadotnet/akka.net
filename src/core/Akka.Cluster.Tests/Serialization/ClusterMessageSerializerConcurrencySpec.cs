//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializerConcurrencySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests.Serialization
{
    /// <summary>
    /// Concurrency regression coverage for <see cref="ClusterMessageSerializer"/>'s lazily-initialized
    /// "use legacy heartbeat message" flag.
    ///
    /// The flag can't be resolved eagerly in the serializer's constructor - serializers are constructed during
    /// <see cref="ActorSystem"/> initialization, before the Cluster extension is registered, so
    /// <c>Cluster.Get(system)</c> would fail if called too early. That makes the lazy-init unavoidable, but it
    /// must be thread-safe: this flag is consulted on every single Heartbeat/HeartbeatRsp
    /// <c>ToBinary</c>/<c>Manifest</c> call, which is one of the hottest paths in a running cluster.
    ///
    /// This spec constructs a FRESH <see cref="ClusterMessageSerializer"/> (so the lazy-init window is still
    /// open) and hammers it with concurrent Heartbeat/HeartbeatRsp Manifest+ToBinary+FromBinary calls from many
    /// threads at once, asserting the manifest and the serialized bytes always agree with each other and that no
    /// exceptions occur.
    /// </summary>
    public class ClusterMessageSerializerConcurrencySpec : AkkaSpec
    {
        public ClusterMessageSerializerConcurrencySpec(ITestOutputHelper output)
            : base("akka.actor.provider = cluster", output)
        {
        }

        [Fact(DisplayName = "ClusterMessageSerializer's lazy legacy-heartbeat-mode init should be thread-safe on a freshly constructed instance")]
        public async Task ClusterMessageSerializer_lazy_init_should_be_thread_safe()
        {
            // Freshly constructed - the lazy `UseLegacyHeartbeatMessage` flag has not been touched yet,
            // so every thread races to resolve it for the first time.
            var serializer = new ClusterMessageSerializer((ExtendedActorSystem)Sys);

            var address = new Address("akka.tcp", "system", "some.host.org", 4711);
            var heartbeat = new ClusterHeartbeatSender.Heartbeat(address, 10, 3);
            var uniqueAddress = new UniqueAddress(address, 17);
            var heartbeatRsp = new ClusterHeartbeatSender.HeartbeatRsp(uniqueAddress, 10, 3);

            // Floor at 4 so single-core CI agents still exercise real concurrency.
            var threadCount = Math.Max(4, Environment.ProcessorCount);
            const int iterations = 5000;

            var exceptions = new ConcurrentBag<Exception>();
            var mismatches = new ConcurrentBag<string>();
            var heartbeatManifests = new ConcurrentBag<string>();
            var heartbeatRspManifests = new ConcurrentBag<string>();

            var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    try
                    {
                        var hbManifest = serializer.Manifest(heartbeat);
                        var hbBytes = serializer.ToBinary(heartbeat);
                        var hbRoundTrip = serializer.FromBinary(hbBytes, hbManifest);
                        heartbeatManifests.Add(hbManifest);

                        if (hbRoundTrip is not ClusterHeartbeatSender.Heartbeat hb || hb.From != address)
                            mismatches.Add($"Heartbeat manifest [{hbManifest}] did not round-trip to a matching Heartbeat: {hbRoundTrip}");

                        var hbRspManifest = serializer.Manifest(heartbeatRsp);
                        var hbRspBytes = serializer.ToBinary(heartbeatRsp);
                        var hbRspRoundTrip = serializer.FromBinary(hbRspBytes, hbRspManifest);
                        heartbeatRspManifests.Add(hbRspManifest);

                        if (hbRspRoundTrip is not ClusterHeartbeatSender.HeartbeatRsp hbRsp || hbRsp.From != uniqueAddress)
                            mismatches.Add($"HeartbeatRsp manifest [{hbRspManifest}] did not round-trip to a matching HeartbeatRsp: {hbRspRoundTrip}");
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            exceptions.Should().BeEmpty(because: "concurrent first-access to the lazy legacy-heartbeat flag must not throw");
            mismatches.Should().BeEmpty(because: "manifest and serialized bytes must always agree on legacy-vs-modern heartbeat format");

            // The lazily-resolved flag must settle on ONE consistent answer for the whole test run - a racy
            // init could otherwise let some calls see the legacy manifest and others the modern one.
            heartbeatManifests.Distinct().Should().HaveCount(1, because: "the legacy-heartbeat flag must resolve to a single, consistent value");
            heartbeatRspManifests.Distinct().Should().HaveCount(1, because: "the legacy-heartbeat flag must resolve to a single, consistent value");
        }
    }
}
