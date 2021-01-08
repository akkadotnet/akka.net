//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonLeavingSpeedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonLeavingSpeedSpec : AkkaSpec
    {

        internal class TheSingleton : UntypedActor
        {
            private readonly IActorRef probe;

            public static Props props(IActorRef probe) => Props.Create(() => new TheSingleton(probe));

            public TheSingleton(IActorRef probe)
            {
                this.probe = probe;
                probe.Tell("started");
            }

            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }

            protected override void PostStop()
            {
                probe.Tell("stopped");
            }
        }

        private readonly ActorSystem[] _systems;
        private readonly TestProbe[] _probes;

        public ClusterSingletonLeavingSpeedSpec() : base(@"
              akka.loglevel = INFO
              akka.actor.provider = ""cluster""
              akka.cluster.auto-down-unreachable-after = 2s

              # With 10 systems and setting min-number-of-hand-over-retries to 5 and gossip-interval to 2s it's possible to
              # reproduce the ClusterSingletonManagerIsStuck and slow hand over in issue #25639
              # akka.cluster.singleton.min-number-of-hand-over-retries = 5
              # akka.cluster.gossip-interval = 2s

              akka.remote {
                dot-netty.tcp {
                  hostname = ""127.0.0.1""
                  port = 0
                }
              }")
        {
            _systems = Enumerable.Range(1, 3).Select(n =>
                ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString($"akka.cluster.roles=[role-{n % 3}]").WithFallback(Sys.Settings.Config))).ToArray();

            _probes = _systems.Select(i => CreateTestProbe()).ToArray();
        }

        public void Join(ActorSystem from, ActorSystem to, IActorRef probe)
        {
            from.ActorOf(ClusterSingletonManager.Props(
                TheSingleton.props(probe),
                PoisonPill.Instance,
                ClusterSingletonManagerSettings.Create(from)), "echo");

            Cluster.Get(from).Join(Cluster.Get(to).SelfAddress);

            Within(TimeSpan.FromSeconds(15), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(from).State.Members.Select(x => x.UniqueAddress).Should().Contain(Cluster.Get(from).SelfUniqueAddress);
                    Cluster.Get(from)
                        .State.Members.Select(x => x.Status)
                        .ToImmutableHashSet()
                        .Should()
                        .Equal(ImmutableHashSet<MemberStatus>.Empty.Add(MemberStatus.Up));
                });
            });
        }

        [Fact]
        public void ClusterSingleton_that_is_leaving_must()
        {
            ClusterSingleton_that_is_leaving_must_join_cluster();
            ClusterSingleton_that_is_leaving_must_quickly_hand_over_to_next_oldest();
        }

        private void ClusterSingleton_that_is_leaving_must_join_cluster()
        {
            for (int i = 0; i < _systems.Length; i++)
                Join(_systems[i], _systems[0], _probes[i]);

            // leader is most likely on system, lowest port
            Join(Sys, _systems[0], TestActor);

            _probes[0].ExpectMsg("started");
        }

        private void ClusterSingleton_that_is_leaving_must_quickly_hand_over_to_next_oldest()
        {
            List<(TimeSpan, TimeSpan)> durations = new List<(TimeSpan, TimeSpan)>();
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for (int i = 0; i < _systems.Length; i++)
            {
                var leaveAddress = Cluster.Get(_systems[i]).SelfAddress;
                CoordinatedShutdown.Get(_systems[i]).Run(CoordinatedShutdown.ClusterLeavingReason.Instance);
                _probes[i].ExpectMsg("stopped", TimeSpan.FromSeconds(10));
                var stoppedDuration = sw.Elapsed;

                if (i != _systems.Length - 1)
                    _probes[i + 1].ExpectMsg("started", TimeSpan.FromSeconds(30));
                else
                    ExpectMsg("started", TimeSpan.FromSeconds(30));

                var startedDuration = sw.Elapsed;

                Within(TimeSpan.FromSeconds(15), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.Get(_systems[i]).IsTerminated.Should().BeTrue();
                        Cluster.Get(Sys).State.Members.Select(m => m.Address).Should().NotContain(leaveAddress);

                        foreach (var sys in _systems)
                        {
                            if (!Cluster.Get(sys).IsTerminated)
                                Cluster.Get(sys).State.Members.Select(m => m.Address).Should().NotContain(leaveAddress);
                        }
                    });
                });

                Log.Info($"Singleton {i} stopped in {(int)stoppedDuration.TotalMilliseconds} ms, started in {(int)startedDuration.Milliseconds} ms, diff ${(int)(startedDuration - stoppedDuration).TotalMilliseconds} ms");
                durations.Add((stoppedDuration, startedDuration));
            }
            sw.Stop();

            for (int i = 0; i < durations.Count; i++)
            {
                Log.Info($"Singleton {i} stopped in {(int)durations[i].Item1.TotalMilliseconds} ms, started in {(int)durations[i].Item2.Milliseconds} ms, diff ${(int)(durations[i].Item2 - durations[i].Item1).TotalMilliseconds} ms");
            }
        }

        protected override void AfterTermination()
        {
            foreach (var s in _systems)
                Shutdown(s);
        }
    }
}
