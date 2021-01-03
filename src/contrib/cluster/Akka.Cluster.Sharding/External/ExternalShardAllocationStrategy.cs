//-----------------------------------------------------------------------
// <copyright file="ExternalShardAllocationStrategy.cs" company="Akka.NET Project">
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

            /// <inheritdoc/>
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

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"GetShardLocation({Shard})";

            #endregion
        }

        public sealed class GetShardLocations : INoSerializationVerificationNeeded
        {
            public static readonly GetShardLocations Instance = new GetShardLocations();

            private GetShardLocations()
            {
            }

            /// <inheritdoc/>
            public override string ToString() => $"GetShardLocations";
        }

        public sealed class GetShardLocationsResponse : INoSerializationVerificationNeeded, IEquatable<GetShardLocationsResponse>
        {
            public GetShardLocationsResponse(IImmutableDictionary<ShardId, Address> desiredAllocations)
            {
                DesiredAllocations = desiredAllocations;
            }

            public IImmutableDictionary<string, Address> DesiredAllocations { get; }

            #region Equals

            /// <inheritdoc/>
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

            /// <inheritdoc/>
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

            /// <inheritdoc/>
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

            /// <inheritdoc/>
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

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Address?.GetHashCode() ?? 0;
            }

            /// <inheritdoc/>
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

            /// <inheritdoc/>
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

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Address.GetHashCode();
            }

            /// <inheritdoc/>
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
            private readonly string typeName;
            private readonly ILoggingAdapter log = Context.GetLogger();
            private readonly IActorRef replicator;
            private readonly LWWDictionaryKey<ShardId, string> Key;
            private IImmutableDictionary<ShardId, string> currentLocations = ImmutableDictionary<ShardId, string>.Empty;

            public static Props Props(string typeName) => Actor.Props.Create(() => new DDataStateActor(typeName));

            public DDataStateActor(string typeName)
            {
                this.typeName = typeName;
                replicator = DistributedData.DistributedData.Get(Context.System).Replicator;
                Key = DdataKey(typeName);
            }

            public IStash Stash { get; set; }

            protected override void PreStart()
            {
                log.Debug("Starting ddata state actor for [{0}]", typeName);
                replicator.Tell(new Subscribe(Key, Self));
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Changed c when c.Key is LWWDictionaryKey<ShardId, string> key:
                        var newLocations = c.Get(key).Entries;
                        currentLocations = currentLocations.SetItems(newLocations);
                        log.Debug("Received updated shard locations [{0}] all locations are now [{1}]",
                            string.Join(", ", newLocations.Select(i => $"{i.Key}: {i.Value}")),
                            string.Join(", ", currentLocations.Select(i => $"{i.Key}: {i.Value}")));
                        return true;
                    case GetShardLocation m:
                        log.Debug("GetShardLocation [{0}]", m.Shard);
                        var shardLoc = currentLocations.GetValueOrDefault(m.Shard); //.map(asStr => AddressFromURIString(asStr));
                        Address shardLocation = null;
                        if (shardLoc != null)
                            ActorPath.TryParseAddress(shardLoc, out shardLocation);
                        Sender.Tell(new GetShardLocationResponse(shardLocation));
                        return true;
                    case GetShardLocations _:
                        log.Debug("GetShardLocations");
                        Sender.Tell(new GetShardLocationsResponse(currentLocations.ToImmutableDictionary(i => i.Key, i =>
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

        private readonly ActorSystem system;
        private readonly string typeName;
        private readonly ILoggingAdapter log;
        private readonly Cluster cluster;

        // local only ask
        private static readonly TimeSpan timeout = TimeSpan.FromSeconds(5);

        private IActorRef shardState;

        protected virtual TimeSpan Timeout => timeout;

        public ExternalShardAllocationStrategy(ActorSystem system, string typeName)
        {
            this.system = system;
            this.typeName = typeName;
            log = Logging.GetLogger(system, GetType());
            cluster = Cluster.Get(system);
        }

        protected virtual IActorRef CreateShardStateActor()
        {
            return system
                .AsInstanceOf<ExtendedActorSystem>()
                .SystemActorOf(DDataStateActor.Props(typeName), $"external-allocation-state-{typeName}");
        }

        public void Start()
        {
            shardState = CreateShardStateActor();
        }

        public async Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations)
        {
            log.Debug("allocateShard [{0}] [{1}] [{2}]", shardId, requester, string.Join(", ", currentShardAllocations.Select(i => $"{i.Key}: {string.Join(", ", i.Value)}")));

            // current shard allocations include all current shard regions
            try
            {
                var slr = await shardState.Ask<GetShardLocationResponse>(new GetShardLocation(shardId), Timeout);

                if (slr.Address == null)
                {
                    log.Debug("No specific location for shard [{0}]. Allocating to requester [{1}]", shardId, requester);
                    return requester;
                }
                else
                {
                    // if it is the local address, convert it so it is found in the shards
                    if (slr.Address.Equals(cluster.SelfAddress))
                    {
                        var localShardRegion = currentShardAllocations.Keys.FirstOrDefault(i => i.Path.Address.HasLocalScope);
                        if (localShardRegion == null)
                        {
                            log.Debug("unable to find local shard in currentShardAllocation. Using requester");
                            return requester;
                        }
                        else
                        {
                            log.Debug("allocating to local shard");
                            return localShardRegion;
                        }
                    }
                    else
                    {
                        var location = currentShardAllocations.Keys.FirstOrDefault(i => i.Path.Address.Equals(slr.Address));
                        if (location == null)
                        {
                            log.Debug("External shard location [{0}] for shard [{1}] not found in members [{2}]",
                                slr.Address,
                                shardId,
                                string.Join(", ", currentShardAllocations.Keys));
                            return requester;
                        }
                        else
                        {
                            log.Debug("Allocating shard to location [{0}]", location);
                            return location;
                        }
                    }
                }
            }
            catch (AskTimeoutException)
            {
                log.Warning("allocate timed out waiting for shard allocation state [{0}]. Allocating to requester [{1}]",
                    shardId,
                    requester);
                return requester;
            }
        }

        public async Task<IImmutableSet<string>> Rebalance(IImmutableDictionary<IActorRef, IImmutableList<string>> currentShardAllocations, IImmutableSet<string> rebalanceInProgress)
        {
            log.Debug("rebalance [{0}] [{1}]",
                string.Join(", ", currentShardAllocations.Select(i => $"{i.Key}: {string.Join(", ", i.Value)}")),
                string.Join(", ", rebalanceInProgress));

            var currentAllocationByAddress = currentShardAllocations.ToImmutableDictionary(i =>
            {
                if (i.Key is IActorRefScope @ref && @ref.IsLocal)
                {
                    // so it can be compared to a address with host and port
                    return cluster.SelfAddress;
                }
                return i.Key.Path.Address;
            }, i => i.Value);

            var currentlyAllocatedShards = currentShardAllocations.SelectMany(i => i.Value).ToImmutableHashSet();

            log.Debug("Current allocations by address: [{0}]", string.Join(", ", currentAllocationByAddress.Select(i => $"{i.Key}: {string.Join(", ", i.Value)}")));

            try
            {
                var desiredMappings = await shardState.Ask<GetShardLocationsResponse>(GetShardLocations.Instance, Timeout);

                log.Debug("desired allocations: [{0}]", string.Join(", ", desiredMappings.DesiredAllocations.Select(i => $"{i.Key}: {i.Value}")));
                var shardsThatNeedRebalanced = desiredMappings.DesiredAllocations.Where(i =>
                {
                    if (currentlyAllocatedShards.Contains(i.Key))
                    {
                        var shards = currentAllocationByAddress.GetValueOrDefault(i.Value);
                        if (shards == null)
                        {
                            log.Debug("Shard [{0}] desired location [{1}] is not part of the cluster, not rebalancing",
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
                        log.Debug("Shard [{0}] not currently allocated so not rebalancing to desired location", i.Key);
                        return false;
                    }
                }).Select(i => i.Key).ToImmutableHashSet();

                if (!shardsThatNeedRebalanced.IsEmpty)
                {
                    log.Debug("Shards not currently in their desired location [{0}]", string.Join(", ", shardsThatNeedRebalanced));
                }
                return shardsThatNeedRebalanced;
            }
            catch (AskTimeoutException)
            {
                log.Warning("rebalance timed out waiting for shard allocation state. Keeping existing allocations");
                return ImmutableHashSet<string>.Empty;
            }
        }
    }
}
