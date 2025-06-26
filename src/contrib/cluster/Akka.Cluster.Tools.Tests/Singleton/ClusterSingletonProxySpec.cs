//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonProxySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit.Abstractions;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonProxySpec : TestKit.Xunit2.TestKit
    {
        public ClusterSingletonProxySpec(ITestOutputHelper output): base(output: output)
        {
        }
        
        [Fact]
        public void ClusterSingletonProxy_must_correctly_identify_the_singleton()
        {
            var seed = new ActorSys();
            seed.Cluster.Join(seed.Cluster.SelfAddress);

            var testSystems =
                Enumerable.Range(0, 4).Select(_ => new ActorSys(joinTo: seed.Cluster.SelfAddress))
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

        [Fact]
        public async Task ClusterSingletonProxy_with_zero_buffering_should_work()
        {
            var seed = new ActorSys();
            seed.Cluster.Join(seed.Cluster.SelfAddress);

            var testSystem = new ActorSys(joinTo: seed.Cluster.SelfAddress, bufferSize: 0);
            
            // have to wait for cluster singleton to be ready, otherwise message will be rejected
            await AwaitConditionAsync(
                () => Task.FromResult(Cluster.Get(testSystem.Sys).State.Members.Count(m => m.Status == MemberStatus.Up) == 2),
                TimeSpan.FromSeconds(30));

            try
            {
                testSystem.TestProxy("Hello");
            }
            finally
            {
                // force everything to cleanup
                Task.WhenAll(testSystem.Sys.Terminate()).Wait(TimeSpan.FromSeconds(30));
            }
        }

        [Fact(DisplayName = "ClusterSingletonProxy should detect if its associated singleton failed to start after a period")]
        public async Task ClusterSingletonProxySingletonTimeoutTest()
        {
            ActorSys seed = null;
            ActorSys testSystem = null;

            try
            {
                seed = new ActorSys(output: Output);
                seed.Cluster.Join(seed.Cluster.SelfAddress);

                // singleton proxy is waiting for a singleton in a non-existent role
                testSystem = new ActorSys(
                    config: """
                            akka.cluster.singleton-proxy {
                                role = "non-existent"
                                log-singleton-identification-failure = true
                                singleton-identification-failure-period = 500ms
                            }
                            """,
                    output: Output);

                testSystem.IgnoreMessages<ClusterSingletonProxy.IdentifySingletonResult>();
                testSystem.Sys.EventStream.Subscribe<ClusterSingletonProxy.IdentifySingletonResult>(testSystem
                    .TestActor);
                testSystem.Cluster.Join(seed.Cluster.SelfAddress);
                await AwaitConditionAsync(() =>
                    Task.FromResult(testSystem.Cluster.State.Members.Count(m => m.Status == MemberStatus.Up) == 2));
                testSystem.IgnoreNoMessages();
                
                // proxy will emit IdentifySingletonTimedOut event locally if it could not find its associated singleton
                // within the detection period
                await AssertTimeoutFired();

                // proxy will continue to emit IdentifySingletonTimedOut event locally if it could not find its
                // associated singleton within the detection period
                await AssertTimeoutFired();
                
                return;
                async Task AssertTimeoutFired()
                {
                    var msg = await testSystem.ExpectMsgAsync<ClusterSingletonProxy.IdentifySingletonResult>(1.Seconds());
                    msg.SingletonName.Should().Be("singleton");
                    msg.Role.Should().Be("non-existent");
                }
            }
            finally
            {
                var tasks = new List<Task>();
                
                if(seed is not null)
                    tasks.Add(seed.Sys.Terminate());
                if(testSystem is not null)
                    tasks.Add(testSystem.Sys.Terminate());
                
                if(tasks.Any())
                    await Task.WhenAll(tasks);
            }
        }

        [Fact(DisplayName = "ClusterSingletonProxy should not start singleton identify detection if a singleton reference already found")]
        public async Task ClusterSingletonProxySingletonTimeoutTest2()
        {
            const string seedConfig = """
                                      akka.cluster {
                                          roles = [seed] # only start singletons on seed role
                                          min-nr-of-members = 1
                                          singleton.role = seed # only start singletons on seed role
                                          singleton-proxy.role = seed # only start singletons on seed role
                                      }
                                      """;
            
            ActorSys seed = null;
            ActorSys seed2 = null;
            ActorSys testSystem = null;

            try
            {
                seed = new ActorSys(config: seedConfig, output: Output);
                seed.Cluster.Join(seed.Cluster.SelfAddress);

                // need to make sure that cluster member age is correct. seed node should be oldest.
                await AwaitConditionAsync(
                    () => Task.FromResult(Cluster.Get(seed.Sys).State.Members.Count(m => m.Status == MemberStatus.Up) == 1),
                    TimeSpan.FromSeconds(30));

                seed2 = new ActorSys(config: seedConfig, output: Output);
                seed2.Cluster.Join(seed.Cluster.SelfAddress);

                // singleton proxy is waiting for a singleton in seed role
                testSystem = new ActorSys(
                    config: """
                            akka.cluster {
                                roles = [proxy]
                                singleton.role = seed # only start singletons on seed role
                                singleton-proxy {
                                    role = seed # only start singletons on seed role
                                    log-singleton-identification-failure = true
                                    singleton-identification-failure-period = 500ms
                                }
                            }
                            """,
                    startSingleton: false,
                    output: Output);

                testSystem.Sys.EventStream.Subscribe<ClusterSingletonProxy.IdentifySingletonResult>(testSystem.TestActor);
                testSystem.Cluster.Join(seed.Cluster.SelfAddress);

                await AwaitAssertAsync(async () =>
                {
                    var result = await testSystem.ExpectMsgAsync<ClusterSingletonProxy.IdentifySingletonResult>();
                    result.Result.Should().Be(ClusterSingletonProxy.IdentifyResult.Success);
                });
                
                testSystem.TestProxy("hello");

                // timeout event should not fire
                await testSystem.ExpectNoMsgAsync(1.Seconds());

                // Second seed node left the cluster, no timeout should be fired because singleton is homed in the first seed
                await seed2.Sys.Terminate();
                
                // wait until MemberRemoved is triggered
                await AwaitConditionAsync(
                    () => Task.FromResult(Cluster.Get(seed.Sys).State.Members.Count(m => m.Status == MemberStatus.Up) == 2),
                    TimeSpan.FromSeconds(30));

                // timeout event should not fire
                await testSystem.ExpectNoMsgAsync(1.Seconds());

                // First seed node which homed the singleton left the cluster
                await seed.Sys.Terminate();
                
                await AwaitAssertAsync(async () =>
                    {
                        var result = await testSystem.ExpectMsgAsync<ClusterSingletonProxy.IdentifySingletonResult>();
                        result.Result.Should().Be(ClusterSingletonProxy.IdentifyResult.Timeout);
                    },
                    TimeSpan.FromSeconds(30));

                // Proxy will emit IdentifySingletonTimedOut event locally because it lost the singleton reference
                // and no nodes are eligible to home the singleton
                await testSystem.ExpectMsgAsync<ClusterSingletonProxy.IdentifySingletonResult>(3.Seconds());
            }
            finally
            {
                var tasks = new List<Task>();
                
                if(seed is not null)
                    tasks.Add(seed.Sys.Terminate());
                if(seed2 is not null)
                    tasks.Add(seed2.Sys.Terminate());
                if(testSystem is not null)
                    tasks.Add(testSystem.Sys.Terminate());
                
                if(tasks.Any())
                    await Task.WhenAll(tasks);
            }
        }
        
        private class ActorSys : TestKit.Xunit2.TestKit
        {
            public Cluster Cluster { get; }

            public ActorSys(string name = "ClusterSingletonProxySystem", Address joinTo = null, int bufferSize = 1000, string config = null, bool startSingleton = true, ITestOutputHelper output = null)
                : base(ActorSystem.Create(
                    name: name, 
                    config: config is null 
                        ? ConfigurationFactory.ParseString(_cfg).WithFallback(DefaultConfig)
                        : ConfigurationFactory.ParseString(config).WithFallback(_cfg).WithFallback(DefaultConfig)), 
                    output: output)
            {
                Cluster = Cluster.Get(Sys);
                if (joinTo != null)
                {
                    Cluster.Join(joinTo);
                }

                if (startSingleton)
                {
                    Cluster.RegisterOnMemberUp(() =>
                    {
                        Sys.ActorOf(ClusterSingletonManager.Props(Props.Create(() => new Singleton()), PoisonPill.Instance,
                            ClusterSingletonManagerSettings.Create(Sys)
                                .WithRemovalMargin(TimeSpan.FromSeconds(5))), "singletonmanager");
                    });
                }

                Proxy =
                    Sys.ActorOf(
                        ClusterSingletonProxy.Props(
                            "user/singletonmanager",
                            ClusterSingletonProxySettings.Create(Sys).WithBufferSize(bufferSize)), 
                        $"singletonProxy-{Cluster.SelfAddress.Port ?? 0}");
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
