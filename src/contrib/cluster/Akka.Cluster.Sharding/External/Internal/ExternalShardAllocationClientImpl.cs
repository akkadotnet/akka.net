//-----------------------------------------------------------------------
// <copyright file="ExternalShardAllocationClientImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly ActorSystem _system;
        private readonly string _typeName;
        private readonly ILoggingAdapter _log;
        private readonly IActorRef _replicator;
        private readonly UniqueAddress _self;
        private readonly TimeSpan _timeout;
        private readonly TimeSpan _askTimeout;
        private readonly LWWDictionaryKey<ShardId, string> _key;

        public ExternalShardAllocationClientImpl(ActorSystem system, string typeName)
        {
            _system = system;
            _typeName = typeName;
            _log = Logging.GetLogger(system, GetType());
            _replicator = DistributedData.DistributedData.Get(system).Replicator;
            _self = Cluster.Get(system).SelfUniqueAddress;

            _timeout =
                system.Settings.Config
                  .GetTimeSpan("akka.cluster.sharding.external-shard-allocation-strategy.client-timeout");
            _askTimeout = _timeout + _timeout;

            _key = ExternalShardAllocationStrategy.DdataKey(typeName);
        }

        public async Task<Done> UpdateShardLocation(string shard, Address location)
        {
            _log.Debug("updateShardLocation {0} {1} key {2}", shard, location, _key);
            switch (await _replicator.Ask(Dsl.Update(_key, LWWDictionary<ShardId, string>.Empty, WriteLocal.Instance, null, existing =>
            {
                return existing.SetItem(_self, shard, location.ToString());
            }), _askTimeout))
            {
                case UpdateSuccess _:
                    return Done.Instance;
                case UpdateTimeout _:
                default:
                    throw new ClientTimeoutException($"Unable to update shard location after ${_timeout}");
            }
        }

        public async Task<ShardLocations> ShardLocations()
        {
            switch (await _replicator.Ask(Dsl.Get(_key, new ReadMajority(_timeout)), _askTimeout))
            {
                case GetSuccess success when success.Key.Equals(_key):
                    return new ShardLocations(success.Get(_key).Entries.ToImmutableDictionary(i => i.Key, i =>
                    {
                        ActorPath.TryParseAddress(i.Value, out var address);
                        return new ExternalShardAllocationStrategy.ShardLocation(address);
                    }));
                case NotFound _:
                    return new ShardLocations(ImmutableDictionary<ShardId, ExternalShardAllocationStrategy.ShardLocation>.Empty);
                case GetFailure _:
                default:
                    throw new ClientTimeoutException($"Unable to get shard locations after ${_timeout}");
            }
        }

        public async Task<Done> UpdateShardLocations(IImmutableDictionary<string, Address> locations)
        {
            _log.Debug("updateShardLocations {0} for {1}", string.Join(", ", locations.Select(i => $"{i.Key}: {i.Value}")), _key);
            switch (await _replicator.Ask(Dsl.Update(_key, LWWDictionary<ShardId, string>.Empty, WriteLocal.Instance, null, existing =>
            {
                var acc = existing;
                foreach (var l in locations)
                    acc = acc.SetItem(_self, l.Key, l.Value.ToString());
                return acc;
            }), _askTimeout))
            {
                case UpdateSuccess _:
                    return Done.Instance;
                case UpdateTimeout _:
                default:
                    throw new ClientTimeoutException($"Unable to update shard location after ${_timeout}");
            }
        }
    }
}
