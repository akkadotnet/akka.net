﻿//-----------------------------------------------------------------------
// <copyright file="MultiNodeClusterShardingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public abstract class MultiNodeClusterShardingSpec<TConfig> : MultiNodeClusterSpec
        where TConfig : MultiNodeClusterShardingConfig
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
                public static readonly Stop Instance = new();

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
                public static readonly Pong Instance = new();

                private Pong()
                {
                }
            }

            public PingPongActor()
            {
                Log.Info($"entity started {0}", Self.Path);
            }

            private ILoggingAdapter _log;
            private ILoggingAdapter Log => _log ??= Context.GetLogger();

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
                public static readonly Stop Instance = new();

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

        private sealed class IntMessageExtractor: IMessageExtractor
        {
            public string EntityId(object message)
                => message switch
                {
                    int id => id.ToString(),
                    _ => null
                };

            public object EntityMessage(object message)
                => message;

            public string ShardId(object message)
                => message switch
                {
                    int id => id.ToString(),
                    _ => null
                };

            public string ShardId(string entityId, object messageHint = null)
                => entityId;
        }

        protected readonly TConfig Config;

        protected readonly Lazy<ClusterShardingSettings> Settings;

        private readonly Lazy<IShardAllocationStrategy> _defaultShardAllocationStrategy;

        protected MultiNodeClusterShardingSpec(TConfig config, Type type)
            : base(config, type)
        {
            this.Config = config;
            ClearStorage();
            EnterBarrier("startup");

            Settings = new Lazy<ClusterShardingSettings>(() =>
            {
                return ClusterShardingSettings.Create(Sys).WithRememberEntities(config.RememberEntities);
            });
            _defaultShardAllocationStrategy = new Lazy<IShardAllocationStrategy>(() =>
            {
                return ClusterSharding.Get(Sys).DefaultShardAllocationStrategy(Settings.Value);
            });
        }

        protected override int InitialParticipantsValueFactory => Roles.Count;


        protected bool IsDdataMode => Config.Mode == StateStoreMode.DData;

        protected bool PersistenceIsNeeded => Config.Mode == StateStoreMode.Persistence
            || Sys.Settings.Config.GetString("akka.cluster.sharding.remember-entities-store").Equals(RememberEntitiesStore.Eventsourced.ToString(), StringComparison.InvariantCultureIgnoreCase);

        private void ClearStorage()
        {
            var path = Sys.Settings.Config.GetString("akka.persistence.snapshot-store.local.dir");
            try
            {
                if (!string.IsNullOrEmpty(path))
                    Directory.Delete(path, true);
            }
            catch (Exception)
            {
            }
        }

        protected override void AfterTermination()
        {
            ClearStorage();
            base.AfterTermination();
        }

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
            IMessageExtractor messageExtractor = null,
            Props entityProps = null,
            ClusterShardingSettings settings = null,
            IShardAllocationStrategy allocationStrategy = null,
            object handOffStopMessage = null)
        {
            return ClusterSharding.Get(sys).Start(
                typeName,
                entityProps ?? SimpleEchoActor.Props(),
                settings ?? Settings.Value,
                messageExtractor ?? new IntMessageExtractor(),
                allocationStrategy ?? _defaultShardAllocationStrategy.Value,
                handOffStopMessage ?? PoisonPill.Instance);
        }

        protected IActorRef StartProxy(
            ActorSystem sys,
            string typeName,
            string role,
            IMessageExtractor messageExtractor = null)
        {
            return ClusterSharding.Get(sys).StartProxy(typeName, role, messageExtractor ?? new IntMessageExtractor());
        }

        protected void SetStoreIfNeeded(ActorSystem sys, RoleName storeOn)
        {
            if (PersistenceIsNeeded)
                SetStore(sys, storeOn);
        }

        protected void SetStore(ActorSystem sys, RoleName storeOn)
        {
            Persistence.Persistence.Instance.Apply(sys);

            var journalProbe = CreateTestProbe(sys);
            sys.ActorSelection(Node(storeOn) / "system" / "akka.persistence.journal.inmem").Tell(new Identify(null), journalProbe.Ref);
            var sharedjournalStore = journalProbe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(20)).Subject;
            sharedjournalStore.Should().NotBeNull();
            MemoryJournalShared.SetStore(sharedjournalStore, sys);

            var snapshotProbe = CreateTestProbe(sys);
            sys.ActorSelection(Node(storeOn) / "system" / "akka.persistence.snapshot-store.inmem").Tell(new Identify(null), snapshotProbe.Ref);
            var sharedSnapshotStore = snapshotProbe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(20)).Subject;
            sharedSnapshotStore.Should().NotBeNull();
            MemorySnapshotStoreShared.SetStore(sharedSnapshotStore, sys);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="startOn">the node to start the `MemoryJournalShared` store on</param>
        protected void StartPersistenceIfNeeded(RoleName startOn, params RoleName[] setStoreOn)
        {
            if (PersistenceIsNeeded)
                StartPersistence(startOn, setStoreOn);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="startOn">the node to start the `MemoryJournalShared` store on</param>
        protected void StartPersistence(RoleName startOn, params RoleName[] setStoreOn)
        {
            Log.Info("Setting up setup shared journal & snapshot.");

            Persistence.Persistence.Instance.Apply(Sys);
            RunOn(() =>
            {
                Persistence.Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.inmem");
                Persistence.Persistence.Instance.Apply(Sys).SnapshotStoreFor("akka.persistence.snapshot-store.inmem");
            }, startOn);

            EnterBarrier("persistence-started");

            RunOn(() =>
            {
                SetStore(Sys, startOn);
            }, setStoreOn);

            EnterBarrier($"after-{startOn.Name}");
        }
    }
}
