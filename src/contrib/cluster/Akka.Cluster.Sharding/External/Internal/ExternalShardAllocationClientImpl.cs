//-----------------------------------------------------------------------
// <copyright file="ExternalShardAllocationClientImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DistributedData;
using Akka.Event;

namespace Akka.Cluster.Sharding.External.Internal
{
    using ShardId = String;

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ExternalShardAllocationClientImpl : IExternalShardAllocationClient
    {
        private readonly ActorSystem system;
        private readonly string typeName;
        private readonly ILoggingAdapter log;
        private readonly IActorRef replicator;
        private readonly UniqueAddress self;
        private readonly TimeSpan timeout;
        private readonly TimeSpan askTimeout;
        private readonly LWWDictionaryKey<ShardId, string> Key;

        public ExternalShardAllocationClientImpl(ActorSystem system, string typeName)
        {
            this.system = system;
            this.typeName = typeName;
            log = Logging.GetLogger(system, GetType());
            replicator = DistributedData.DistributedData.Get(system).Replicator;
            self = Cluster.Get(system).SelfUniqueAddress;

            timeout =
                system.Settings.Config
                  .GetTimeSpan("akka.cluster.sharding.external-shard-allocation-strategy.client-timeout");
            askTimeout = timeout + timeout;

            Key = ExternalShardAllocationStrategy.DdataKey(typeName);
        }

        public async Task<Done> UpdateShardLocation(string shard, Address location)
        {
            log.Debug("updateShardLocation {0} {1} key {2}", shard, location, Key);
            switch (await replicator.Ask(Dsl.Update(Key, LWWDictionary<ShardId, string>.Empty, WriteLocal.Instance, null, existing =>
            {
                return existing.SetItem(self, shard, location.ToString());
            }), askTimeout))
            {
                case UpdateSuccess _:
                    return Done.Instance;
                case UpdateTimeout _:
                default:
                    throw new ClientTimeoutException($"Unable to update shard location after ${timeout}");
            }
        }

        public async Task<ShardLocations> ShardLocations()
        {
            switch (await replicator.Ask(Dsl.Get(Key, new ReadMajority(timeout)), askTimeout))
            {
                case GetSuccess success when success.Key.Equals(Key):
                    return new ShardLocations(success.Get(Key).Entries.ToImmutableDictionary(i => i.Key, i =>
                    {
                        ActorPath.TryParseAddress(i.Value, out var address);
                        return new ExternalShardAllocationStrategy.ShardLocation(address);
                    }));
                case NotFound _:
                    return new ShardLocations(ImmutableDictionary<ShardId, ExternalShardAllocationStrategy.ShardLocation>.Empty);
                case GetFailure _:
                default:
                    throw new ClientTimeoutException($"Unable to get shard locations after ${timeout}");
            }
        }

        public async Task<Done> UpdateShardLocations(IImmutableDictionary<string, Address> locations)
        {
            log.Debug("updateShardLocations {0} for {1}", string.Join(", ", locations.Select(i => $"{i.Key}: {i.Value}")), Key);
            switch (await replicator.Ask(Dsl.Update(Key, LWWDictionary<ShardId, string>.Empty, WriteLocal.Instance, null, existing =>
            {
                var acc = existing;
                foreach (var l in locations)
                    acc = acc.SetItem(self, l.Key, l.Value.ToString());
                return acc;
            }), askTimeout))
            {
                case UpdateSuccess _:
                    return Done.Instance;
                case UpdateTimeout _:
                default:
                    throw new ClientTimeoutException($"Unable to update shard location after ${timeout}");
            }
        }
    }
}
