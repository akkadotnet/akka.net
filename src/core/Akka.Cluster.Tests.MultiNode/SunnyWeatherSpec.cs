//-----------------------------------------------------------------------
// <copyright file="SunnyWeatherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class SunnyWeatherNodeConfig : MultiNodeConfig
    {
        public RoleName First { get; set; }

        public RoleName Second { get; set; }

        public RoleName Third { get; set; }

        public RoleName Fourth { get; set; }

        public RoleName Fifth { get; set; }

        public SunnyWeatherNodeConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.loggers = [""Akka.TestKit.TestEventListener, Akka.TestKit""]
                akka.loglevel = INFO
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.failure-detector.monitored-by-nr-of-members = 3
            ");
        }
    }

    public class SunnyWeatherSpec : MultiNodeClusterSpec
    {
        private class Listener : UntypedActor
        {
            private readonly AtomicReference<SortedSet<Member>> _unexpected;

            public Listener(AtomicReference<SortedSet<Member>> unexpected)
            {
                _unexpected = unexpected;
            }

            protected override void OnReceive(object message)
            {
                message.Match()
                    .With<ClusterEvent.IMemberEvent>(evt =>
                    {
                        _unexpected.Value.Add(evt.Member);
                    })
                    .With<ClusterEvent.CurrentClusterState>(() =>
                    {
                        // ignore
                    });
            }
        }

        private readonly SunnyWeatherNodeConfig _config;

        public SunnyWeatherSpec() : this(new SunnyWeatherNodeConfig())
        {
        }

        protected SunnyWeatherSpec(SunnyWeatherNodeConfig config) : base(config, typeof(SunnyWeatherSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void SunnyWeatherSpecs()
        {
            Normal_cluster_must_be_healthy();
        }

        public void Normal_cluster_must_be_healthy()
        {
            // start some
            AwaitClusterUp(_config.First, _config.Second, _config.Third);
            RunOn(() =>
            {
                Log.Debug("3 joined");
            }, _config.First, _config.Second, _config.Third);

            // add a few more
            AwaitClusterUp(Roles.ToArray());
            Log.Debug("5 joined");

            var unexpected = new AtomicReference<SortedSet<Member>>(new SortedSet<Member>());
            Cluster.Subscribe(Sys.ActorOf(Props.Create(() => new Listener(unexpected))), new[]
            {
                typeof(ClusterEvent.IMemberEvent)
            });

            foreach (var n in Enumerable.Range(1, 30))
            {
                EnterBarrier("period-" + n);
                unexpected.Value.Should().BeEmpty();
                AwaitMembersUp(Roles.Count);
                AssertLeaderIn(Roles);
                if (n % 5 == 0)
                {
                    Log.Debug("Passed period [{0}]", n);
                }
                Thread.Sleep(1000);
            }

            EnterBarrier("after");
        }
    }
}
