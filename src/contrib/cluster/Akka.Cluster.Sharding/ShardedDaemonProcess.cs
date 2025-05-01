//-----------------------------------------------------------------------
// <copyright file="ShardedDaemonProcess.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Routing;
using Akka.Util.Internal;

namespace Akka.Cluster.Sharding
{
    internal sealed class KeepAlivePinger : UntypedActor, IWithTimers
    {
        private sealed class Tick
        {
            public static Tick Instance { get; } = new();

            private Tick()
            {
            }
        }

        public string Name { get; }
        public string[] Identities { get; }
        public IActorRef ShardingRef { get; }
        public ShardedDaemonProcessSettings Settings { get; }

        public ITimerScheduler Timers { get; set; } = null!; // gets set by Akka.NET

        public static Props Props(ShardedDaemonProcessSettings settings, string name, string[] identities,
            IActorRef shardingRef) =>
            Actor.Props.Create(() => new KeepAlivePinger(settings, name, identities, shardingRef));

        public KeepAlivePinger(ShardedDaemonProcessSettings settings, string name, string[] identities,
            IActorRef shardingRef)
        {
            Settings = settings;
            Name = name;
            Identities = identities;
            ShardingRef = shardingRef;
        }

        protected override void PreStart()
        {
            base.PreStart();

            TriggerStartAll();
            Context.System.Log.Debug(
                "Starting Sharded Daemon Process KeepAlivePinger for [{0}], with ping interval [{1}]",
                Name, Settings.KeepAliveInterval);
            Timers.StartPeriodicTimer("tick", Tick.Instance, Settings.KeepAliveInterval);
        }

        protected override void OnReceive(object message)
        {
            if (message is Tick)
            {
                TriggerStartAll();
                Context.System.Log.Debug("Periodic ping sent to [{0}] processes", Identities.Length);
            }
        }

        private void TriggerStartAll() => Identities.ForEach(id => ShardingRef.Tell(new ShardRegion.StartEntity(id)));
    }

    internal sealed class MessageExtractor : HashCodeMessageExtractor
    {
        public MessageExtractor(int maxNumberOfShards)
            : base(maxNumberOfShards)
        {
        }

        public override string? EntityId(object message) => (message as ShardingEnvelope)?.EntityId;
        
        // Due to https://github.com/akkadotnet/akka.net/issues/7470 we will want to return the underlying content too
        public override object? EntityMessage(object message) => (message as ShardingEnvelope)?.Message ?? message;

        public override string ShardId(string entityId, object? messageHint = null)
        {
            return entityId;
        }
    }

    /// <summary>
    /// Used to support push-based communication with the <see cref="ShardedDaemonProcess"/> - uses
    /// round-robin messaging to distribute messages to the actors in the cluster.
    /// </summary>
    /// <remarks>
    /// NOTE: does not use a traditional <see cref="Router"/> to distribute messages because we want to use the
    /// <see cref="ShardRegion"/> mechanism in order to piggyback off of its reliability and message buffering
    /// features, which <see cref="Router"/> and <see cref="ActorSelection"/> do not support.
    /// </remarks>
    internal sealed class DaemonMessageRouter : UntypedActor
    {
        private readonly string[] _entityIds;
        private readonly IActorRef _shardingRef;
        private int _index = 0;

        public DaemonMessageRouter(string[] entityIds, IActorRef shardingRef)
        {
            _entityIds = entityIds;
            // validate that we have at least 1 entityId
            if (_entityIds.Length == 0)
                throw new ArgumentException("At least one entityId must be provided", nameof(entityIds));
            _shardingRef = shardingRef;
        }

        protected override void OnReceive(object message)
        {
            if (message is Broadcast broadcast)
            {
                var unwrapped = broadcast.Message;
                foreach (var entityId in _entityIds)
                {
                    _shardingRef.Forward(new ShardingEnvelope(entityId, unwrapped));
                }
            }
            else
            {
                var nextId = _entityIds[_index % _entityIds.Length];
            
                // have to remember to always allow the sharding envelope to be forwarded
                _shardingRef.Forward(new ShardingEnvelope(nextId, message));
                if (_index == int.MaxValue) _index = 0;
                else _index++;
            }
        }
    }

    /// <summary>
    /// <para>This extension runs a pre-set number of actors in a cluster.</para>
    /// <para>
    /// The typical use case is when you have a task that can be divided in a number of workers, each doing a
    /// sharded part of the work, for example consuming the read side events from Akka Persistence through
    /// tagged events where each tag decides which consumer that should consume the event.
    /// </para>
    /// <para>Each named set needs to be started on all the nodes of the cluster on start up.</para>
    /// <para>
    /// The processes are spread out across the cluster, when the cluster topology changes the processes may be stopped
    /// and started anew on a new node to rebalance them.
    /// </para>
    /// <para>Not for user extension.</para>
    /// </summary>
    [DoNotInherit, ApiMayChange]
    public class ShardedDaemonProcess : IExtension
    {
        private readonly ExtendedActorSystem _system;

        public ShardedDaemonProcess(ExtendedActorSystem system) => _system = system;

        public static ShardedDaemonProcess Get(ActorSystem system) =>
            system.WithExtension<ShardedDaemonProcess, ShardedDaemonProcessExtensionProvider>();

        /// <summary>
        /// Start a specific number of actors that is then kept alive in the cluster.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <param name="numberOfInstances">TBD</param>
        /// <param name="propsFactory">Given a unique id of `0` until `numberOfInstance` create an entity actor.</param>
        /// <returns>A reference to a router actor that will distribute all messages evenly across the workers
        /// using round-robin message routing. <c>null</c> if the ShardedDaemonProcess is misconfigured.</returns>
        public IActorRef? Init(string name, int numberOfInstances, Func<int, Props> propsFactory) =>
            Init(name, numberOfInstances, propsFactory, ShardedDaemonProcessSettings.Create(_system), null);

        /// <summary>
        /// Start a specific number of actors that is then kept alive in the cluster.
        /// </summary>
        /// <param name="name">The name of this sharded daemon set</param>
        /// <param name="numberOfInstances">The number of instances to run</param>
        /// <param name="propsFactory">Given a unique id of `0` until `numberOfInstance` create an entity actor.</param>
        /// <param name="stopMessage">Sent to the actors when they need to stop because of a rebalance across the nodes of the cluster or cluster shutdown.</param>
        /// <returns>A reference to a router actor that will distribute all messages evenly across the workers
        /// using round-robin message routing. <c>null</c> if the ShardedDaemonProcess is misconfigured.</returns>
        public IActorRef? Init(string name, int numberOfInstances, Func<int, Props> propsFactory, object? stopMessage) =>
            Init(name, numberOfInstances, propsFactory, ShardedDaemonProcessSettings.Create(_system), stopMessage);

        /// <summary>
        /// Start a specific number of actors, each with a unique numeric id in the set, that is then kept alive in the cluster.
        /// </summary>
        /// <param name="name">The name of this sharded daemon set</param>
        /// <param name="numberOfInstances">The number of instances to run</param>
        /// <param name="propsFactory">Given a unique id of `0` until `numberOfInstance` create an entity actor.</param>
        /// <param name="settings">The settings for configuring this sharded daemon process.</param>
        /// <param name="stopMessage">If defined sent to the actors when they need to stop because of a rebalance across the nodes of the cluster or cluster shutdown.</param>
        /// <returns>A reference to a router actor that will distribute all messages evenly across the workers
        /// using round-robin message routing. <c>null</c> if the ShardedDaemonProcess is misconfigured.</returns>
        public IActorRef? Init(string name, int numberOfInstances, Func<int, Props> propsFactory,
            ShardedDaemonProcessSettings settings, object? stopMessage)
        {
            // One shard per actor identified by the numeric id encoded in the entity id
            var numberOfShards = numberOfInstances;
            var entityIds = Enumerable.Range(0, numberOfInstances).Select(i => i.ToString()).ToArray();

            // Defaults in `akka.cluster.sharding` but allow overrides specifically for sharded-daemon-process
            var shardingBaseSettings = settings.ShardingSettings;
            if (shardingBaseSettings == null)
            {
                var shardingConfig = _system.Settings.Config.GetConfig("akka.cluster.sharded-daemon-process.sharding");
                var coordinatorSingletonConfig =
                    _system.Settings.Config.GetConfig(shardingConfig.GetString("coordinator-singleton"));
                shardingBaseSettings = ClusterShardingSettings.Create(shardingConfig, coordinatorSingletonConfig);
            }

            var shardingSettings = new ClusterShardingSettings(
                settings.Role ?? shardingBaseSettings.Role,
                false, // remember entities disabled
                "",
                "",
                TimeSpan.Zero, // passivation disabled
                StateStoreMode.DData,
                shardingBaseSettings.TuningParameters,
                shardingBaseSettings.CoordinatorSingletonSettings,
                shardingBaseSettings.LeaseSettings);

            if (string.IsNullOrEmpty(shardingSettings.Role) ||
                Cluster.Get(_system).SelfRoles.Contains(shardingSettings.Role))
            {
                var sharding = ClusterSharding.Get(_system);
                var shardingRef = sharding.Start(
                    typeName: FormatWorkerProcessName(name),
                    entityPropsFactory: entityId => propsFactory(int.Parse(entityId)),
                    settings: shardingSettings,
                    messageExtractor: new MessageExtractor(numberOfShards),
                    allocationStrategy: sharding.DefaultShardAllocationStrategy(shardingSettings),
                    stopMessage ?? PoisonPill.Instance);

                _system.ActorOf(
                    KeepAlivePinger.Props(settings, name, entityIds, shardingRef),
                    $"ShardedDaemonProcessKeepAlive-{name}");
                
                return _system.ActorOf(
                    Props.Create(() =>
                        new DaemonMessageRouter(entityIds,
                            shardingRef)), $"ShardedDaemonProcessRouter-{name}");
            }
            
            _system.Log.Warning(
                "ShardedDaemonProcess [{0}] not started because the role [{1}] is not included in the self roles [{2}]",
                name, shardingSettings.Role, string.Join(", ", Cluster.Get(_system).SelfRoles));

            return null;
        }
        
        private static string FormatWorkerProcessName(string name) => $"sharded-daemon-process-{name}";

        /// <summary>
        /// Starts a proxy for a sharded daemon process running in a different role.
        /// </summary>
        /// <param name="name">The name of this daemon worker set</param>
        /// <param name="numberOfInstances">The number of instances that belong on this set -
        /// note that this value must match the value used in the `Init` call on the hosting-nodes
        /// or messages may be lost.</param>
        /// <param name="role">The role where the worker actors are hosted.</param>
        /// <returns>A reference to a router actor that will distribute all messages evenly across the workers
        /// using round-robin message routing.</returns>
        public IActorRef InitProxy(string name, int numberOfInstances, string role)
        {
            // create a shard region proxy so we can access daemon workers running in a different role
            var sharding = ClusterSharding.Get(_system);
            var shardingRef = sharding.StartProxy(
                typeName: FormatWorkerProcessName(name),
                role: role,
                messageExtractor: new MessageExtractor(numberOfInstances));

            return _system.ActorOf(
                Props.Create(() =>
                    new DaemonMessageRouter(Enumerable.Range(0, numberOfInstances).Select(i => i.ToString()).ToArray(),
                        shardingRef)), $"ShardedDaemonProcessProxyRouter-{name}");
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class ShardedDaemonProcessExtensionProvider : ExtensionIdProvider<ShardedDaemonProcess>
    {
        public override ShardedDaemonProcess CreateExtension(ExtendedActorSystem system) => new(system);
    }
}
