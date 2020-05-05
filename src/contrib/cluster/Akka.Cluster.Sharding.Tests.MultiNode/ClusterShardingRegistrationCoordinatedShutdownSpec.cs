//-----------------------------------------------------------------------
// <copyright file="ClusterShardingRegistrationCoordinatedShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using System.Collections.Immutable;
using System.IO;
using Akka.Util;
using FluentAssertions;
using System.Threading.Tasks;
using System.Threading;
using Akka.Event;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingRegistrationCoordinatedShutdownSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingRegistrationCoordinatedShutdownSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString($@"
                    akka.actor {{
                        serializers {{
                            hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                        }}
                        serialization-bindings {{
                            ""System.Object"" = hyperion
                        }}
                    }}
                    akka.loglevel = DEBUG
                    akka.actor.provider = cluster
                    akka.remote.log-remote-lifecycle-events = off
                    akka.cluster.sharding.state-store-mode = ""ddata""
                    "))
                .WithFallback(Sharding.ClusterSharding.DefaultConfig())
                .WithFallback(Tools.Singleton.ClusterSingletonManager.DefaultConfig())
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterShardingRegistrationCoordinatedShutdownSpec : MultiNodeClusterSpec
    {
        #region setup

        internal sealed class StopEntity
        {
            public static readonly StopEntity Instance = new StopEntity();

            private StopEntity()
            {
            }
        }

        internal class Entity : ActorBase
        {
            private readonly ILoggingAdapter _log = Context.GetLogger();
            protected override bool Receive(object message)
            {
                _log.Warning("Entity received [{0}]", message);
                switch (message)
                {
                    case int id:
                        Sender.Tell(id);
                        return true;
                    case StopEntity _:
                        Context.Stop(Self);
                        return true;
                }
                return false;
            }
        }

        internal ExtractEntityId extractEntityId = message => message is int ? (message.ToString(), message) : Option<(string, object)>.None;

        internal ExtractShardId extractShardId = message => message is int ? message.ToString() : null;

        private readonly Lazy<IActorRef> _region;

        private readonly ClusterShardingRegistrationCoordinatedShutdownSpecConfig _config;

        public ClusterShardingRegistrationCoordinatedShutdownSpec()
            : this(new ClusterShardingRegistrationCoordinatedShutdownSpecConfig())
        {
        }

        protected ClusterShardingRegistrationCoordinatedShutdownSpec(ClusterShardingRegistrationCoordinatedShutdownSpecConfig config)
            : base(config, typeof(ClusterShardingRegistrationCoordinatedShutdownSpec))
        {
            _config = config;
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
            EnterBarrier("startup");
        }

        #endregion

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(to));
                StartSharding();
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void StartSharding()
        {
            var allocationStrategy = new LeastShardAllocationStrategy(2, 1);
            ClusterSharding.Get(Sys).Start(
                typeName: "Entity",
                entityProps: Props.Create<Entity>(),
                settings: ClusterShardingSettings.Create(Sys),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId,
                allocationStrategy: allocationStrategy,
                handOffStopMessage: StopEntity.Instance);
        }

        [MultiNodeFact]
        public void ClusterShardingRegistrationCoordinatedShutdownSpecs()
        {
            ClusterSharding_Region_registration_during_CoordinatedShutdown_must_try_next_oldest();
        }

        public void ClusterSharding_Region_registration_during_CoordinatedShutdown_must_try_next_oldest()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                // second should be oldest
                Join(_config.Second, _config.Second);
                Join(_config.First, _config.Second);
                Join(_config.Third, _config.Second);

                AwaitAssert(() =>
                {
                    Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3);
                });

                var probe = CreateTestProbe();
                var csTaskDone = CreateTestProbe();
                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).AddTask(CoordinatedShutdown.PhaseBeforeClusterShutdown, "test", () =>
                    {
                        Thread.Sleep(200);
                        _region.Value.Tell(1, probe.Ref);
                        probe.ExpectMsg(1);
                        csTaskDone.Ref.Tell(Done.Instance);
                        return Task.FromResult(Done.Instance);
                    });
                }, _config.Third);


                StartSharding();

                EnterBarrier("before-shutdown");

                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.UnknownReason.Instance);
                    AwaitCondition(() => Cluster.IsTerminated);
                }, _config.Second);

                RunOn(() =>
                {
                    CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.UnknownReason.Instance);
                    AwaitCondition(() => Cluster.IsTerminated);
                    csTaskDone.ExpectMsg<Done>();
                }, _config.Third);

                EnterBarrier("after-shutdown");

                RunOn(() =>
                {
                    _region.Value.Tell(2);
                    ExpectMsg(2);
                    LastSender.Path.Address.HasLocalScope.Should().BeTrue();
                }, _config.First);

                EnterBarrier("after-1");
            });
        }
    }
}
