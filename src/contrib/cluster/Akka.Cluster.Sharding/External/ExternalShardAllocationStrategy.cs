//-----------------------------------------------------------------------
// <copyright file="ExternalShardAllocationStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DistributedData;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Cluster.Sharding.External
{
    using ShardId = String;

    public class ExternalShardAllocationStrategy : IStartableAllocationStrategy
    {
        // local only messages

        public sealed class GetShardLocation : INoSerializationVerificationNeeded, IEquatable<GetShardLocation>
        {
            public GetShardLocation(ShardId shard)
            {
                Shard = shard;
            }

            public string Shard { get; }

            #region Equals
            
            public override bool Equals(object obj)
            {
                return Equals(obj as GetShardLocation);
            }

            public bool Equals(GetShardLocation other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            public override string ToString() => $"GetShardLocation({Shard})";

            #endregion
        }

        public sealed class GetShardLocations : INoSerializationVerificationNeeded
        {
            public static readonly GetShardLocations Instance = new();

            private GetShardLocations()
            {
            }

            public override string ToString() => "GetShardLocations";
        }

        public sealed class GetShardLocationsResponse : INoSerializationVerificationNeeded, IEquatable<GetShardLocationsResponse>
        {
            public GetShardLocationsResponse(IImmutableDictionary<ShardId, Address> desiredAllocations)
            {
                DesiredAllocations = desiredAllocations;
            }

            public IImmutableDictionary<string, Address> DesiredAllocations { get; }

            #region Equals
            
            public override bool Equals(object obj)
            {
                return Equals(obj as GetShardLocationsResponse);
            }

            public bool Equals(GetShardLocationsResponse other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return DesiredAllocations.SequenceEqual(other.DesiredAllocations);
            }
            
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var s in DesiredAllocations)
                        hashCode = (hashCode * 397) ^ s.Key.GetHashCode();
                    return hashCode;
                }
            }
            
            public override string ToString() => $"GetShardLocationsResponse({string.Join(", ", DesiredAllocations.Select(i => $"{i.Key}: {i.Value}"))})";

            #endregion
        }

        public sealed class GetShardLocationResponse : INoSerializationVerificationNeeded, IEquatable<GetShardLocationResponse>
        {
            public GetShardLocationResponse(Address address)
            {
                Address = address;
            }

            public Address Address { get; }

            #region Equals

            public override bool Equals(object obj)
            {
                return Equals(obj as GetShardLocationResponse);
            }

            public bool Equals(GetShardLocationResponse other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Equals(Address, other.Address);
            }

            public override int GetHashCode()
            {
                return Address?.GetHashCode() ?? 0;
            }

            public override string ToString() => $"GetShardLocationResponse({Address})";

            #endregion
        }

        // only returned locally, serialized as a string
        public sealed class ShardLocation : INoSerializationVerificationNeeded, IEquatable<ShardLocation>
        {
            public ShardLocation(Address address)
            {
                Address = address;
            }

            public Address Address { get; }

            #region Equals
            
            public override bool Equals(object obj)
            {
                return Equals(obj as ShardLocation);
            }

            public bool Equals(ShardLocation other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Address.Equals(other.Address);
            }
            
            public override int GetHashCode()
            {
                return Address.GetHashCode();
            }
            
            public override string ToString() => $"ShardLocation({Address})";

            #endregion
        }

        /// <summary>
        /// uses a string primitive types are optimized in ddata to not serialize every entity separately
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>
        internal static LWWDictionaryKey<ShardId, string> DdataKey(string typeName)
        {
            return new LWWDictionaryKey<ShardId, string>($"external-sharding-{typeName}");
        }

        private class DDataStateActor : ActorBase, IWithUnboundedStash
        {
            private readonly string _typeName;
            private readonly ILoggingAdapter _log = Context.GetLogger();
            private readonly IActorRef _replicator;
            private readonly LWWDictionaryKey<ShardId, string> _key;
            private IImmutableDictionary<ShardId, string> _currentLocations = ImmutableDictionary<ShardId, string>.Empty;

            public static Props Props(string typeName) => Actor.Props.Create(() => new DDataStateActor(typeName));

            public DDataStateActor(string typeName)
            {
                _typeName = typeName;
                _replicator = DistributedData.DistributedData.Get(Context.System).Replicator;
                _key = DdataKey(typeName);
            }

            public IStash Stash { get; set; }

            protected override void PreStart()
            {
                _log.Debug("Starting ddata state actor for [{0}]", _typeName);
                _replicator.Tell(new Subscribe(_key, Self));
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Changed c when c.Key is LWWDictionaryKey<ShardId, string> key:
                        var newLocations = c.Get(key).Entries;
                        _currentLocations = _currentLocations.SetItems(newLocations);
                        _log.Debug("Received updated shard locations [{0}] all locations are now [{1}]",
                            string.Join(", ", newLocations.Select(i => $"{i.Key}: {i.Value}")),
                            string.Join(", ", _currentLocations.Select(i => $"{i.Key}: {i.Value}")));
                        return true;
                    case GetShardLocation m:
                        _log.Debug("GetShardLocation [{0}]", m.Shard);
                        var shardLoc = _currentLocations.GetValueOrDefault(m.Shard); //.map(asStr => AddressFromURIString(asStr));
                        Address shardLocation = null;
                        if (shardLoc != null)
                            ActorPath.TryParseAddress(shardLoc, out shardLocation);
                        Sender.Tell(new GetShardLocationResponse(shardLocation));
                        return true;
                    case GetShardLocations _:
                        _log.Debug("GetShardLocations");
                        Sender.Tell(new GetShardLocationsResponse(_currentLocations.ToImmutableDictionary(i => i.Key, i =>
                        {
                            /*AddressFromURIString(asStr)*/
                            ActorPath.TryParseAddress(i.Value, out var address);
                            return address;
                        })));
                        return true;
                }
                return false;
            }
        }

        private readonly ActorSystem _system;
        private readonly string _typeName;
        private readonly ILoggingAdapter _log;
        private readonly Cluster _cluster;

        // local only ask
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

        private IActorRef _shardState;

        protected virtual TimeSpan Timeout => _timeout;

        public ExternalShardAllocationStrategy(ActorSystem system, string typeName)
        {
            _system = system;
            _typeName = typeName;
            _log = Logging.GetLogger(system, GetType());
            _cluster = Cluster.Get(system);
        }

        protected virtual IActorRef CreateShardStateActor()
        {
            return _system
                .AsInstanceOf<ExtendedActorSystem>()
                .SystemActorOf(DDataStateActor.Props(_typeName), $"external-allocation-state-{_typeName}");
        }

        public void Start()
        {
            _shardState = CreateShardStateActor();
        }

        public async Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations)
        {
            _log.Debug("allocateShard [{0}] [{1}] [{2}]", shardId, requester, string.Join(", ", currentShardAllocations.Select(i => $"{i.Key}: {string.Join(", ", i.Value)}")));

            // current shard allocations include all current shard regions
            try
            {
                var slr = await _shardState.Ask<GetShardLocationResponse>(new GetShardLocation(shardId), Timeout);

                if (slr.Address == null)
                {
                    _log.Debug("No specific location for shard [{0}]. Allocating to requester [{1}]", shardId, requester);
                    return requester;
                }
                else
                {
                    // if it is the local address, convert it so it is found in the shards
                    if (slr.Address.Equals(_cluster.SelfAddress))
                    {
                        var localShardRegion = currentShardAllocations.Keys.FirstOrDefault(i => i.Path.Address.HasLocalScope);
                        if (localShardRegion == null)
                        {
                            _log.Debug("unable to find local shard in currentShardAllocation. Using requester");
                            return requester;
                        }
                        else
                        {
                            _log.Debug("allocating to local shard");
                            return localShardRegion;
                        }
                    }
                    else
                    {
                        var location = currentShardAllocations.Keys.FirstOrDefault(i => i.Path.Address.Equals(slr.Address));
                        if (location == null)
                        {
                            _log.Debug("External shard location [{0}] for shard [{1}] not found in members [{2}]",
                                slr.Address,
                                shardId,
                                string.Join(", ", currentShardAllocations.Keys));
                            return requester;
                        }
                        else
                        {
                            _log.Debug("Allocating shard to location [{0}]", location);
                            return location;
                        }
                    }
                }
            }
            catch (AskTimeoutException)
            {
                _log.Warning("allocate timed out waiting for shard allocation state [{0}]. Allocating to requester [{1}]",
                    shardId,
                    requester);
                return requester;
            }
        }

        public async Task<IImmutableSet<string>> Rebalance(IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations, IImmutableSet<string> rebalanceInProgress)
        {
            _log.Debug("rebalance [{0}] [{1}]",
                string.Join(", ", currentShardAllocations.Select(i => $"{i.Key}: {string.Join(", ", i.Value)}")),
                string.Join(", ", rebalanceInProgress));

            var currentAllocationByAddress = currentShardAllocations.ToImmutableDictionary(i =>
            {
                if (i.Key is IActorRefScope @ref && @ref.IsLocal)
                {
                    // so it can be compared to a address with host and port
                    return _cluster.SelfAddress;
                }
                return i.Key.Path.Address;
            }, i => i.Value);

            var currentlyAllocatedShards = currentShardAllocations.SelectMany(i => i.Value).ToImmutableHashSet();

            _log.Debug("Current allocations by address: [{0}]", string.Join(", ", currentAllocationByAddress.Select(i => $"{i.Key}: {string.Join(", ", i.Value)}")));

            try
            {
                var desiredMappings = await _shardState.Ask<GetShardLocationsResponse>(GetShardLocations.Instance, Timeout);

                _log.Debug("desired allocations: [{0}]", string.Join(", ", desiredMappings.DesiredAllocations.Select(i => $"{i.Key}: {i.Value}")));
                var shardsThatNeedRebalanced = desiredMappings.DesiredAllocations.Where(i =>
                {
                    if (currentlyAllocatedShards.Contains(i.Key))
                    {
                        var shards = currentAllocationByAddress.GetValueOrDefault(i.Value);
                        if (shards == null)
                        {
                            _log.Debug("Shard [{0}] desired location [{1}] is not part of the cluster, not rebalancing",
                                i.Key,
                                i.Value);
                            return false; // not a current allocation so don't rebalance yet
                        }
                        else
                        {
                            var inCorrectLocation = shards.Contains(i.Key);
                            return !inCorrectLocation;
                        }
                    }
                    else
                    {
                        _log.Debug("Shard [{0}] not currently allocated so not rebalancing to desired location", i.Key);
                        return false;
                    }
                }).Select(i => i.Key).ToImmutableHashSet();

                if (!shardsThatNeedRebalanced.IsEmpty)
                {
                    _log.Debug("Shards not currently in their desired location [{0}]", string.Join(", ", shardsThatNeedRebalanced));
                }
                return shardsThatNeedRebalanced;
            }
            catch (AskTimeoutException)
            {
                _log.Warning("rebalance timed out waiting for shard allocation state. Keeping existing allocations");
                return ImmutableHashSet<string>.Empty;
            }
        }
    }
}
