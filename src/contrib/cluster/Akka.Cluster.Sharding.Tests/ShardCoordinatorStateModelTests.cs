﻿// //-----------------------------------------------------------------------
// // <copyright file="ShardCoordinatorStateModelTests.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

#if NET5_0_OR_GREATER

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tests;
using Akka.Tests.Shared.Internals.Helpers;
using FsCheck;
using FsCheck.Experimental;


namespace Akka.Cluster.Sharding.Tests
{
    using ShardId = System.String;

    public class ClusterShardingGenerator
    {
        public const string ShardRegionName = "myRegion";
        
        public static Arbitrary<IActorRef> ShardRegionRefGenerator(bool proxy = false)
        {
            var gen1 = ClusterGenerators.AddressGenerator().Generator; // node addresses
            var gen3 = Arb.Default.Int64().Generator; // IActorRef UIDs


            Func<Address, long, IActorRef> combiner = (node, actorUid) =>
            {
                var path = (new RootActorPath(node) / "system" / "sharding" / (proxy ? ShardRegionName + "-proxy" : ShardRegionName) )
                    .WithUid(actorUid);
                return new EmptyLocalActorRef(null, path, null);
            };
            var producer = FsharpDelegateHelper.Create(combiner);

            return Arb.From(Gen.Map2(producer, gen1, gen3));
        }
    }

    internal sealed class StateHolder
    {
        /// <summary>
        /// Has to be part of a mutable class in order for mutations
        /// to be remembered in-between FsCheck operations.
        /// </summary>
        public PersistentShardCoordinator.State State { get; set; }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <param name="Command">Current command being processed.</param>
    /// <param name="Success">True if processed successfully, false if throwing.</param>
    internal record CommandHistoryItem(PersistentShardCoordinator.IDomainEvent Command, bool Success);
    
    /// <summary>
    /// Immutable model mirroring the <see cref="PersistentShardCoordinator.State"/> object.
    /// </summary>
    internal sealed record TestState
    {
        /// <summary>
        /// Stack of recently executed commands
        /// </summary>
        public ImmutableStack<CommandHistoryItem> Commands { get; init; }

        /// <summary>
        /// All shard regions participating in the test.
        /// </summary>
        public ImmutableHashSet<IActorRef> AvailableShardRegions { get; init; }
        
        /// <summary>
        /// All shard region proxies participating in the test.
        /// </summary>
        public ImmutableHashSet<IActorRef> AvailableShardRegionProxies { get; init; }

        /// <summary>
        /// Region for each shard.
        /// </summary>
        public IImmutableDictionary<ShardId, IActorRef> Shards { get; init; }

        /// <summary>
        /// Shards for each region.
        /// </summary>
        public IImmutableDictionary<IActorRef, IImmutableList<ShardId>> Regions { get; init; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableSet<IActorRef> RegionProxies { get; init; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableSet<ShardId> UnallocatedShards { get; init; }

        public bool RememberEntities { get; init; }
    }

    internal class StateModel : Machine<StateHolder, TestState>
    {
        public StateModel()
        {
            var gen0 = Gen.Choose(0, 100); // shardCount
            var gen1 = ClusterShardingGenerator.ShardRegionRefGenerator().Generator.ArrayOf(10); //shardRegions
            var gen2 = ClusterShardingGenerator.ShardRegionRefGenerator(true).Generator.ArrayOf(10); // shardRegionProxies

            Func<int, IActorRef[], IActorRef[], Setup<StateHolder, TestState>> combinedFunc = (i, refs, arg3) =>
                new ClusterStateSetup(i, refs, arg3);

            var fsharpFunc = FsharpDelegateHelper.Create(combinedFunc);

            var composedGenerator = Gen.Map3(fsharpFunc, gen0, gen1, gen2);

            Setup = Arb.From(composedGenerator);
        }
        
        public override Gen<Operation<StateHolder, TestState>> Next(TestState obj0)
        {
            throw new System.NotImplementedException();
        }

        public override Arbitrary<Setup<StateHolder, TestState>> Setup { get; }

        #region Setup Classes

        /// <summary>
        /// Used to populate the test
        /// </summary>
        // ReSharper disable once ClassNeverInstantiated.Local
        private class ClusterStateSetup : Setup<StateHolder, TestState>
        {
            public ClusterStateSetup(int shardCount, IActorRef[] regions, IActorRef[] proxies)
            {
                ShardCount = shardCount;
                Regions = regions;
                Proxies = proxies;
            }

            public int ShardCount { get; }
            
            public IActorRef[] Regions { get; }
            
            public IActorRef[] Proxies { get; }

            public override StateHolder Actual()
            {
                return new StateHolder() { State = PersistentShardCoordinator.State.Empty };
            }

            public override TestState Model()
            {
                return new TestState();
            }
        }

        #endregion
        
        #region Operation Classes

        public abstract class ShardOperationBase : Operation<StateHolder, TestState>
        {
            public abstract PersistentShardCoordinator.IDomainEvent Event { get; }

            /// <summary>
            /// Checks keys only - does not check shard-specific values.
            /// </summary>
            /// <param name="actual">The actual data from the sharding implementation.</param>
            /// <param name="model">Data from our FsCheck model.</param>
            /// <returns>A set of falsifiable FsCheck properties.</returns>
            public Property CheckCommonShardStates(StateHolder actual, TestState model)
            {
                var exceptRegions = actual.State.Regions.Keys.ToImmutableHashSet()
                    .SymmetricExcept(model.Regions.Keys);
                var exceptProxies = actual.State.RegionProxies.SymmetricExcept(model.RegionProxies);
                var exceptShards = actual.State.Shards.Keys.ToImmutableHashSet().SymmetricExcept(model.Shards.Keys);
                var exceptUnallocatedShards = actual.State.UnallocatedShards.SymmetricExcept(model.UnallocatedShards);

                return actual.State.Regions.Keys.ToImmutableHashSet().SetEquals(model.Regions.Keys)
                    .Label(
                        $"Both ShardRegions should contain same set of members. Instead found members not included in both sequences: [{string.Join(",", exceptRegions)}]")
                    .And(actual.State.Shards.Keys.ToImmutableHashSet().SetEquals(model.Shards.Keys)
                        .Label(
                            $"Both Shards should contain same set of members. Instead found members not included in both sequences: [{string.Join(",", exceptShards)}]"))
                    .And(actual.State.RegionProxies.SetEquals(model.RegionProxies).Label(
                        $"Both ShardProxies should contain same set of members. Instead found members not included in both sequences: [{string.Join(",", exceptProxies)}]"))
                    .And(actual.State.UnallocatedShards.SetEquals(model.UnallocatedShards)
                        .Label(
                            $"Both UnallocatedShards should contain same set of members. Instead found members not included in both sequences: [{string.Join(",", exceptUnallocatedShards)}]"));
            }

            public Property CheckShardRegionSpecificStates(StateHolder actual, TestState model, IActorRef shardRegion)
            {
                var regionDiff = actual.State.Regions[shardRegion].ToImmutableHashSet()
                    .SymmetricExcept(model.Regions[shardRegion]);

                return actual.State.Regions[shardRegion].ToImmutableHashSet().SetEquals(model.Regions[shardRegion])
                    .Label(
                        $"Both ShardRegions should contain shards, but found members not included in both sequences: [{string.Join(",", regionDiff)}]");
            }

            public Property CheckShardSpecificStates(StateHolder actual, TestState model, ShardId shard, bool unallocated = false)
            {
                Property CheckContains()
                {
                    return ((actual.State.Shards.ContainsKey(shard) || model.Shards.ContainsKey(shard)) == !unallocated)
                        .Label(
                            $"Shard [{shard}] should not be present in Shards collection (because, unallocated), but found in state: {actual.State.Shards.ContainsKey(shard)} && model {model.Shards.ContainsKey(shard)}")
                        .And(((actual.State.UnallocatedShards.Contains(shard) &&
                               model.UnallocatedShards.Contains(shard)) == unallocated)
                            .Label(
                                $"Shard [{shard}] should be unallocated, but found in state: {model.UnallocatedShards.Contains(shard)} && model {model.UnallocatedShards.Contains(shard)}"));
                }

                if (unallocated)
                    return CheckContains();

                return CheckContains().And(actual.State.Shards[shard].Equals(model.Shards[shard])
                    .Label(
                        $"Expected Shard [{shard}] to have registered ShardRegion [{model.Shards[shard]}], but found [{actual.State.Shards[shard]}]"));
            }

            public Property ShouldThrowException(StateHolder actual, string errMsgContains)
            {
                Action throwable = () =>
                {
                    actual.State = actual.State.Updated(Event);
                };

                Exception trapped = null;

                try
                {
                    throwable();
                }
                catch (Exception ex)
                {
                    trapped = ex;
                }

                return (trapped != null && trapped.Message.Contains(errMsgContains)).Label(
                    $"Expected state to throw error containing message [{errMsgContains}] when processing {Event}. Instead found [{trapped}]");
            }
        }

        public class RegisterShardRegion : ShardOperationBase
        {
            public RegisterShardRegion(IActorRef shardRegion)
            {
                ShardRegion = shardRegion;
            }

            public IActorRef ShardRegion { get; }

            public override PersistentShardCoordinator.IDomainEvent Event =>  new PersistentShardCoordinator.ShardRegionRegistered(ShardRegion);

            public override Property Check(StateHolder actual, TestState model)
            {
                // check for whether we should expect an exception
                if (!model.Commands.Peek().Success)
                {
                    return ShouldThrowException(actual, "already registered").And(CheckCommonShardStates(actual, model))
                        .And(CheckShardRegionSpecificStates(actual, model, ShardRegion));
                }

                actual.State = actual.State.Updated(Event);
                return CheckCommonShardStates(actual, model)
                    .And(CheckShardRegionSpecificStates(actual, model, ShardRegion));
            }

            public override TestState Run(TestState obj0)
            {

                // don't want to wipe out our ShardRegion here if this event was unexpected
                if (obj0.Regions.ContainsKey(ShardRegion))
                    return obj0 with { Commands = obj0.Commands.Push(new CommandHistoryItem(Event, false))};
                return obj0 with { Regions = obj0.Regions.Add(ShardRegion, ImmutableList<string>.Empty), Commands = obj0.Commands.Push(new CommandHistoryItem(Event, true)) };
            }
        }

        public class ShardRegionProxyRegistered : ShardOperationBase
        {
            public ShardRegionProxyRegistered(IActorRef shardRegionProxy)
            {
                ShardRegionProxy = shardRegionProxy;
            }

            public override Property Check(StateHolder actual, TestState model)
            {
                // check for whether we should expect an exception
                if (!model.Commands.Peek().Success)
                {
                    return ShouldThrowException(actual, "already registered").And(CheckCommonShardStates(actual, model));
                }
                
                actual.State = actual.State.Updated(Event);
                return CheckCommonShardStates(actual, model);
            }

            public override TestState Run(TestState obj0)
            {
                // don't want to wipe out our ShardRegion here if this event was unexpected
                if (obj0.RegionProxies.Contains(ShardRegionProxy))
                    return obj0 with { Commands = obj0.Commands.Push(new CommandHistoryItem(Event, false)) };
                return obj0 with { RegionProxies = obj0.RegionProxies.Add(ShardRegionProxy), Commands = obj0.Commands.Push(new CommandHistoryItem(Event, true)) };
            }
            
            public IActorRef ShardRegionProxy { get; }

            public override PersistentShardCoordinator.IDomainEvent Event =>
                new PersistentShardCoordinator.ShardRegionProxyRegistered(ShardRegionProxy);
        }

        public sealed class ShardRegionTerminated : ShardOperationBase
        {
            public ShardRegionTerminated(IActorRef shardRegion)
            {
                ShardRegion = shardRegion;
            }

            public IActorRef ShardRegion { get; }
            
            public override Property Check(StateHolder actual, TestState model)
            {
                // check for whether we should expect an exception
                if (!model.Commands.Peek().Success)
                {
                    return ShouldThrowException(actual, "Terminated region").And(CheckCommonShardStates(actual, model))
                        .And(CheckShardRegionSpecificStates(actual, model, ShardRegion));
                }

                actual.State = actual.State.Updated(Event);
                return CheckCommonShardStates(actual, model)
                    .And(CheckShardRegionSpecificStates(actual, model, ShardRegion));
            }

            public override TestState Run(TestState obj0)
            {
                var ob1 = obj0 with
                {
                    // can't re-use terminated actors
                    AvailableShardRegions = obj0.AvailableShardRegions.Remove(ShardRegion)
                };
                
                if (ob1.Regions.TryGetValue(ShardRegion, out var shards))
                {
                    var unallocatedShards =
                        ob1.RememberEntities ? ob1.UnallocatedShards.Union(shards) : ob1.UnallocatedShards;

                    return ob1 with
                    {
                        UnallocatedShards = unallocatedShards, 
                        Regions = ob1.Regions.Remove(ShardRegion),
                        Shards = ob1.Shards.RemoveRange(shards),
                        Commands = ob1.Commands.Push(new CommandHistoryItem(Event, true))
                    };
                }

                // ShardRegion not found - actual should throw an exception
                return ob1 with { Commands = ob1.Commands.Push(new CommandHistoryItem(Event, false)) };
            }

            public override PersistentShardCoordinator.IDomainEvent Event =>
                new PersistentShardCoordinator.ShardRegionTerminated(ShardRegion);
        }

        public sealed class ShardRegionProxyTerminated : ShardOperationBase
        {
            public ShardRegionProxyTerminated(IActorRef shardRegionProxy)
            {
                ShardRegionProxy = shardRegionProxy;
            }

            public IActorRef ShardRegionProxy { get; }
            
            public override Property Check(StateHolder actual, TestState model)
            {
                // check for whether we should expect an exception
                if (!model.Commands.Peek().Success)
                {
                    return ShouldThrowException(actual, "Terminated region proxy").And(CheckCommonShardStates(actual, model));
                }
                
                actual.State = actual.State.Updated(Event);
                return CheckCommonShardStates(actual, model);
            }

            public override TestState Run(TestState obj0)
            {
                var success = obj0.RegionProxies.Contains(ShardRegionProxy);
                
                var ob1 = obj0 with
                {
                    Commands = obj0.Commands.Push(new CommandHistoryItem(Event, success)),
                    // can't re-use terminated actors
                    AvailableShardRegionProxies = obj0.AvailableShardRegionProxies.Remove(ShardRegionProxy),
                    RegionProxies = obj0.RegionProxies.Remove(ShardRegionProxy)
                };

                return ob1;
            }

            public override PersistentShardCoordinator.IDomainEvent Event =>
                new PersistentShardCoordinator.ShardRegionProxyTerminated(ShardRegionProxy);
        }

        public sealed class ShardHomeAllocated : ShardOperationBase
        {
            public ShardHomeAllocated(string shardId, IActorRef shardRegion)
            {
                ShardId = shardId;
                ShardRegion = shardRegion;
            }

            public ShardId ShardId { get; }
            
            public IActorRef ShardRegion {get;}
            
            public override Property Check(StateHolder actual, TestState model)
            {
                // check for whether we should expect an exception
                if (!model.Commands.Peek().Success)
                {
                    return ShouldThrowException(actual, "").And(CheckCommonShardStates(actual, model))
                        .And(CheckShardSpecificStates(actual, model, ShardId, !model.RememberEntities));
                }
                
                actual.State = actual.State.Updated(Event);
                return CheckCommonShardStates(actual, model).And(CheckShardSpecificStates(actual, model, ShardId, !model.RememberEntities));
            }

            public override TestState Run(TestState obj0)
            {
                if (obj0.Regions.TryGetValue(ShardRegion, out var currentShards) && !obj0.Shards.ContainsKey(ShardId))
                {
                    var unallocatedShards = obj0.RememberEntities
                        ? obj0.UnallocatedShards.Remove(ShardId)
                        : obj0.UnallocatedShards;
                    return obj0 with
                    {
                        Shards = obj0.Shards.SetItem(ShardId, ShardRegion),
                        Regions = obj0.Regions.SetItem(ShardRegion, currentShards.Add(ShardId)),
                        UnallocatedShards = unallocatedShards,
                        Commands = obj0.Commands.Push(new CommandHistoryItem(Event, true)),
                    };
                }
                
                // we have an illegal state that is going to throw
                return obj0 with { Commands = obj0.Commands.Push(new CommandHistoryItem(Event, false)) };
            }

            public override PersistentShardCoordinator.IDomainEvent Event =>
                new PersistentShardCoordinator.ShardHomeAllocated(ShardId, ShardRegion);
        }

        public sealed class ShardHomeDeallocated : ShardOperationBase
        {
            public ShardHomeDeallocated(string shardId)
            {
                ShardId = shardId;
            }

            public ShardId ShardId { get; }
            
            public override Property Check(StateHolder actual, TestState model)
            {
                // check for whether we should expect an exception
                if (!model.Commands.Peek().Success)
                {
                    return ShouldThrowException(actual, "").And(CheckCommonShardStates(actual, model))
                        .And(CheckShardSpecificStates(actual, model, ShardId, !model.RememberEntities));
                }
                
                actual.State = actual.State.Updated(Event);
                return CheckCommonShardStates(actual, model).And(CheckShardSpecificStates(actual, model, ShardId, !model.RememberEntities));
            }

            public override TestState Run(TestState obj0)
            {
                if (obj0.Shards.TryGetValue(ShardId, out var region) &&
                    obj0.Regions.TryGetValue(region, out var regionShards))
                {
                    var newUnallocatedShards = obj0.RememberEntities
                        ? obj0.UnallocatedShards.Remove(ShardId)
                        : obj0.UnallocatedShards;

                    return obj0 with
                    {
                        Commands = obj0.Commands.Push(new CommandHistoryItem(Event, true)),
                        Shards = obj0.Shards.Remove(ShardId),
                        Regions = obj0.Regions.SetItem(region, regionShards.Remove(ShardId)),
                        UnallocatedShards = newUnallocatedShards
                    };
                }

                return obj0 with { Commands = obj0.Commands.Push(new CommandHistoryItem(Event, false)) };
            }

            public override PersistentShardCoordinator.IDomainEvent Event =>
                new PersistentShardCoordinator.ShardHomeDeallocated(ShardId);
        }

        #endregion
    }

    public class ShardCoordinatorStateModelTests
    {
    }
}

#endif