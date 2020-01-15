//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonProxySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonProxySpec 
    {
        [Fact]
        public void ClusterSingletonProxy_must_correctly_identify_the_singleton()
        {
            var seed = new ActorSys();
            seed.Cluster.Join(seed.Cluster.SelfAddress);

            var testSystems =
                Enumerable.Range(0, 4).Select(x => new ActorSys(joinTo: seed.Cluster.SelfAddress))
                .Concat(new[] {seed})
                .ToList();

            try
            {
                testSystems.ForEach(s => s.TestProxy("Hello"));
                testSystems.ForEach(s => s.TestProxy("World"));
            }
            finally
            {
                // force everything to cleanup
                Task.WhenAll(testSystems.Select(s => s.Sys.Terminate()))
                    .Wait(TimeSpan.FromSeconds(30));
            }
        }

        private class ActorSys : TestKit.Xunit2.TestKit
        {
            public Cluster Cluster { get; }

            public ActorSys(string name = "ClusterSingletonProxySystem", Address joinTo = null)
                : base(ActorSystem.Create(name, ConfigurationFactory.ParseString(_cfg).WithFallback(TestKit.Configs.TestConfigs.DefaultConfig)))
            {
                Cluster = Cluster.Get(Sys);
                if (joinTo != null)
                {
                    Cluster.Join(joinTo);
                }

                Cluster.RegisterOnMemberUp(() =>
                {
                    Sys.ActorOf(ClusterSingletonManager.Props(Props.Create(() => new Singleton()), PoisonPill.Instance,
                        ClusterSingletonManagerSettings.Create(Sys)
                            .WithRemovalMargin(TimeSpan.FromSeconds(5))), "singletonmanager");
                });

                Proxy =
                    Sys.ActorOf(ClusterSingletonProxy.Props("user/singletonmanager",
                        ClusterSingletonProxySettings.Create(Sys)), $"singletonProxy-{Cluster.SelfAddress.Port ?? 0}");
            }

            public IActorRef Proxy { get; private set; }

            public void TestProxy(string msg)
            {
                var probe = CreateTestProbe();
                probe.Send(Proxy, msg);

                // 25 seconds to make sure the singleton was started up
                probe.ExpectMsg("Got " + msg, TimeSpan.FromSeconds(25));
            }

            private static readonly string _cfg = @"
                akka {
                  loglevel = INFO
                  cluster {
                    auto-down-unreachable-after = 10s
                    min-nr-of-members = 2
                  }
                  actor.provider = ""cluster""
                  remote {
                    log-remote-lifecycle-events = off
                    dot-netty.tcp
                        {
                            hostname = ""127.0.0.1""
                            port = 0
                        }
                 }
              }";
        }

        private class Singleton : UntypedActor
        {
            private readonly ILoggingAdapter _log = Context.GetLogger();

            protected override void PreStart()
            {
                _log.Info("Singleton created on {0}", Cluster.Get(Context.System).SelfAddress);
            }

            protected override void OnReceive(object message)
            {
                Sender.Tell("Got " + message);
            }
        }
    }
}
