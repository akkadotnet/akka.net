//-----------------------------------------------------------------------
// <copyright file="SerializationSupportConcurrencySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Akka.DistributedData.Serialization;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests.Serialization
{
    /// <summary>
    /// Concurrency regression coverage for <see cref="SerializationSupport"/>'s lazily-initialized
    /// <c>Serialization</c>/<c>AddressProtocol</c>/<c>TransportInfo</c> properties.
    ///
    /// Each of these used to be a racy check-then-write over a plain <c>volatile</c> field: concurrent first
    /// callers could each construct their OWN (expensive, and behaviorally divergent - it owns its own type
    /// binding cache) <see cref="Akka.Serialization.Serialization"/> instance. This sits on the CRDT/gossip
    /// remote path via <see cref="ReplicatorMessageSerializer"/> and <see cref="ReplicatedDataSerializer"/>.
    /// </summary>
    [Collection("DistributedDataSpec")]
    public class SerializationSupportConcurrencySpec : TestKit.Xunit.TestKit
    {
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
                provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            }
            akka.remote.dot-netty.tcp.port = 0").WithFallback(DistributedData.DefaultConfig());

        public SerializationSupportConcurrencySpec(ITestOutputHelper output)
            : base(BaseConfig, nameof(SerializationSupportConcurrencySpec), output: output)
        {
        }

        [Fact(DisplayName = "SerializationSupport should construct its lazy Serialization/AddressProtocol/TransportInfo exactly once under concurrent first access")]
        public async Task SerializationSupport_lazy_init_should_be_thread_safe()
        {
            // Freshly constructed - none of the lazy properties have been touched yet.
            var support = new SerializationSupport((ExtendedActorSystem)Sys);

            // Floor at 4 so single-core CI agents still exercise real concurrency.
            var threadCount = Math.Max(4, Environment.ProcessorCount);

            var serializationInstances = new ConcurrentBag<Akka.Serialization.Serialization>();
            var protocols = new ConcurrentBag<string>();
            var transportInfos = new ConcurrentBag<Akka.Serialization.Information>();
            var exceptions = new ConcurrentBag<Exception>();

            // Barrier maximizes the odds that every thread races into the lazy-init window at the same time.
            using var barrier = new Barrier(threadCount);

            var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    serializationInstances.Add(support.Serialization);
                    protocols.Add(support.AddressProtocol);
                    transportInfos.Add(support.TransportInfo);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            exceptions.Should().BeEmpty(because: "concurrent first access to the lazy properties must not throw");
            serializationInstances.Should().HaveCount(threadCount);
            serializationInstances.Distinct().Should().HaveCount(1, because: "exactly one Serialization instance must ever be constructed");
            protocols.Distinct().Should().HaveCount(1, because: "exactly one address protocol value must ever be resolved");
            transportInfos.Distinct().Should().HaveCount(1, because: "exactly one TransportInfo instance must ever be constructed");
        }

        [Fact(DisplayName = "ReplicatorMessageSerializer should round-trip distinct payloads concurrently through OtherMessageToProto/FromProto without corruption")]
        public async Task ReplicatorMessageSerializer_concurrent_round_trip_should_be_thread_safe()
        {
            // Freshly constructed - the underlying SerializationSupport's lazy fields are unresolved.
            var serializer = new ReplicatorMessageSerializer((ExtendedActorSystem)Sys);

            // Floor at 4 so single-core CI agents still exercise real concurrency.
            var threadCount = Math.Max(4, Environment.ProcessorCount);
            const int iterations = 300;

            var exceptions = new ConcurrentBag<Exception>();
            var mismatches = new ConcurrentBag<string>();

            var tasks = Enumerable.Range(0, threadCount).Select(t => Task.Run(() =>
            {
                for (var i = 0; i < iterations; i++)
                {
                    var tag = $"thread-{t}-iter-{i}";
                    var key = new GSetKey<string>(tag);
                    var data = GSet.Create(tag);
                    var msg = new GetSuccess(key, null, data);

                    try
                    {
                        var bytes = serializer.ToBinary(msg);
                        var manifest = serializer.Manifest(msg);
                        var round = serializer.FromBinary(bytes, manifest);

                        if (round is not GetSuccess success || !Equals(success, msg))
                            mismatches.Add($"[{tag}] expected {msg}, got {round}");
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                    }
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            exceptions.Should().BeEmpty(because: "concurrent OtherMessageToProto/FromProto calls must not share a racy, partially-constructed Serialization instance");
            mismatches.Should().BeEmpty(because: "each thread's payload must round-trip independently of every other thread's");
        }
    }
}
