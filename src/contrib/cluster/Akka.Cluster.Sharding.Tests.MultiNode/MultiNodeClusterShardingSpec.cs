//-----------------------------------------------------------------------
// <copyright file="MultiNodeClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class MultiNodeClusterShardingSpec : MultiNodeClusterSpec
    {
        protected class EntityActor : ActorBase
        {
            public sealed class Started
            {
                public Started(IActorRef @ref)
                {
                    Ref = @ref;
                }
                public IActorRef Ref { get; }
            }

            public EntityActor(IActorRef probe)
            {
                Probe = probe;
                probe.Tell(new Started(Self));
            }

            public IActorRef Probe { get; }

            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        protected class PingPongActor : ActorBase
        {
            public class Stop
            {
                public static readonly Stop Instance = new Stop();

                private Stop()
                {
                }
            }

            public class Ping
            {
                public Ping(long id)
                {
                    Id = id;
                }

                public long Id { get; }
            }

            public class Pong
            {
                public static readonly Pong Instance = new Pong();

                private Pong()
                {
                }
            }

            public PingPongActor()
            {
                Log.Info($"entity started {0}", Self.Path);
            }

            private ILoggingAdapter _log;
            private ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Stop _:
                        Context.Stop(Self);
                        return true;
                    case Ping _:
                        Sender.Tell(Pong.Instance);
                        return true;
                }
                return false;
            }
        }

        protected class ShardedEntity : ActorBase
        {
            public class Stop
            {
                public static readonly Stop Instance = new Stop();

                private Stop()
                {
                }
            }

            public ShardedEntity()
            {
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case int id:
                        Sender.Tell(id);
                        return true;
                    case Stop _:
                        Context.Stop(Self);
                        return true;
                }
                return false;
            }
        }

        internal ExtractEntityId IntExtractEntityId = message =>
        {
            if (message is int id)
                return (id.ToString(), message);
            return Option<(string, object)>.None;
        };

        internal ExtractShardId IntExtractShardId = message =>
        {
            switch (message)
            {
                case int id:
                    return id.ToString();
                case ShardRegion.StartEntity se:
                    return se.EntityId;
            }
            return null;
        };

        private readonly MultiNodeClusterShardingConfig config;

        private readonly Lazy<ClusterShardingSettings> settings;

        private readonly Lazy<IShardAllocationStrategy> defaultShardAllocationStrategy;

        protected MultiNodeClusterShardingSpec(MultiNodeClusterShardingConfig config, Type type)
            : base(config, type)
        {
            this.config = config;
            settings = new Lazy<ClusterShardingSettings>(() =>
            {
                return ClusterShardingSettings.Create(Sys).WithRememberEntities(config.RememberEntities);
            });
            defaultShardAllocationStrategy = new Lazy<IShardAllocationStrategy>(() =>
            {
                return ClusterSharding.Get(Sys).DefaultShardAllocationStrategy(settings.Value);
            });
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;


        protected bool IsDdataMode => config.Mode == ClusterShardingSettings.StateStoreModeDData;

        protected bool PersistenceIsNeeded => config.Mode == ClusterShardingSettings.StateStoreModePersistence;

        /// <summary>
        /// Flexible cluster join pattern usage.
        /// </summary>
        /// <param name="from">the node the `Cluster.join` is `runOn`</param>
        /// <param name="to">to the node to join to</param>
        /// <param name="onJoinedRunOnFrom">optionally execute a function after join validation is successful, e.g. start sharding or create coordinator</param>
        /// <param name="assertNodeUp">if disabled - false, the joining member's `MemberStatus.Up`
        ///     and similar assertions are not run. This allows tests that were
        ///     not doing assertions (e.g. ClusterShardingMinMembersSpec) or
        ///     doing them after `onJoinedRunOnFrom` more flexibility.
        ///     Defaults to true, running member status checks.</param>
        /// <param name="max"></param>
        protected void Join(
               RoleName from,
               RoleName to,
               Action onJoinedRunOnFrom = null,
               bool assertNodeUp = true,
               TimeSpan? max = null)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                if (assertNodeUp)
                {
                    Within(max ?? TimeSpan.FromSeconds(20), () =>
                     {
                         AwaitAssert(() =>
                         {
                             Cluster.State.IsMemberUp(Node(from).Address).Should().BeTrue();
                         });
                     });
                }
                onJoinedRunOnFrom?.Invoke();
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        protected IActorRef StartSharding(
            ActorSystem sys,
            string typeName,
            Props entityProps = null,
            ClusterShardingSettings settings = null,
            ExtractEntityId extractEntityId = null,
            ExtractShardId extractShardId = null,
            IShardAllocationStrategy allocationStrategy = null,
            object handOffStopMessage = null)
        {
            return ClusterSharding.Get(sys).Start(
                typeName,
                entityProps ?? EchoActor.Props(this),
                settings ?? this.settings.Value,
                extractEntityId ?? IntExtractEntityId,
                extractShardId ?? IntExtractShardId,
                allocationStrategy ?? defaultShardAllocationStrategy.Value,
                handOffStopMessage ?? PoisonPill.Instance);
        }

        protected IActorRef StartProxy(
            ActorSystem sys,
            string typeName,
            string role,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId)
        {
            return ClusterSharding.Get(sys).StartProxy(typeName, role, extractEntityId, extractShardId);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="startOn">the node to start the `MemoryJournalShared` store on</param>
        protected void StartPersistenceIfNeeded(RoleName startOn)
        {
            if (PersistenceIsNeeded)
                StartPersistence(startOn);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="startOn">the node to start the `MemoryJournalShared` store on</param>
        protected void StartPersistence(RoleName startOn)
        {
            Log.Info("Setting up setup shared journal.");

            // start the Persistence extension
            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Persistence.Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.MemoryJournal");
            }, startOn);
            EnterBarrier("persistence-started");

            Sys.ActorSelection(Node(startOn) / "system" / "akka.persistence.journal.MemoryJournal").Tell(new Identify(null));
            var sharedStore = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(10)).Subject;
            sharedStore.Should().NotBeNull();

            MemoryJournalShared.SetStore(sharedStore, Sys);

            EnterBarrier("persistence-started-test");

            //check persistence running
            var probe = CreateTestProbe(Sys);
            var journal = Persistence.Persistence.Instance.Get(Sys).JournalFor(null);
            journal.Tell(new Persistence.ReplayMessages(0, 0, long.MaxValue, Guid.NewGuid().ToString(), probe.Ref));
            probe.ExpectMsg<Persistence.RecoverySuccess>(TimeSpan.FromSeconds(10));

            EnterBarrier($"after-{startOn.Name}");
        }
    }
}
