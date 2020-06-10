//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonLeaseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using DotNetty.Common.Concurrency;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class ClusterSingletonLeaseSpec : AkkaSpec
    {
        internal class ImportantSingleton : ActorBase
        {
            private readonly IActorRef _lifeCycleProbe;
            private readonly ILoggingAdapter _log = Context.GetLogger();

            public ImportantSingleton(IActorRef lifeCycleProbe)
            {
                _lifeCycleProbe = lifeCycleProbe;
            }

            protected override void PreStart()
            {
                _log.Info("Important Singleton Starting");
                _lifeCycleProbe.Tell("preStart");
            }

            protected override void PostStop()
            {
                _log.Info("Important Singleton Stopping");
                _lifeCycleProbe.Tell("postStop");
                base.PostStop();
            }

            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }


        public class TestException : Exception
        {
            public TestException(string message) : base(message)
            {
            }

            public TestException(string message, Exception innerEx)
                : base(message, innerEx)
            {
            }

            protected TestException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }

        private Cluster cluster;
        private TestLeaseExt testLeaseExt;

        private AtomicCounter counter = new AtomicCounter(0);
        private TimeSpan shortDuration = TimeSpan.FromMilliseconds(50);
        private string leaseOwner;

        public ClusterSingletonLeaseSpec() : base(ConfigurationFactory.ParseString(@"
              #akka.loglevel = INFO
              akka.loglevel = DEBUG
              akka.actor.provider = ""cluster""

              akka.cluster.singleton {
                 use-lease = ""test-lease""
                 lease-retry-interval = 2000ms
              }

              akka.remote {
                dot-netty.tcp {
                  hostname = ""127.0.0.1""
                  port = 0
                }
              }").WithFallback(TestLease.Configuration))
        {

            cluster = Cluster.Get(Sys);
            testLeaseExt = TestLeaseExt.Get(Sys);

            leaseOwner = cluster.SelfMember.Address.HostPort();

            cluster.Join(cluster.SelfAddress);
            AwaitAssert(() =>
            {
                cluster.SelfMember.Status.ShouldBe(MemberStatus.Up);
            });
        }

        private string NextName() => $"important-{counter.GetAndIncrement()}";

        private ClusterSingletonManagerSettings NextSettings() => ClusterSingletonManagerSettings.Create(Sys).WithSingletonName(NextName());

        private string LeaseNameFor(ClusterSingletonManagerSettings settings) => $"AkkaSpec-singleton-akka://AkkaSpec/user/{settings.SingletonName}";

        [Fact]
        public void ClusterSingleton_with_lease_should_not_start_until_lease_is_available()
        {
            var probe = CreateTestProbe();
            var settings = NextSettings();

            Sys.ActorOf(
                ClusterSingletonManager.Props(Props.Create(() => new ImportantSingleton(probe.Ref)), PoisonPill.Instance, settings),
                settings.SingletonName);

            TestLease testLease = null;
            AwaitAssert(() =>
            {
                testLease = testLeaseExt.GetTestLease(LeaseNameFor(settings));
            }); // allow singleton manager to create the lease

            probe.ExpectNoMsg(shortDuration);
            testLease.InitialPromise.SetResult(true);
            probe.ExpectMsg("preStart");
        }

        [Fact]
        public void ClusterSingleton_with_lease_should_do_not_start_if_lease_acquire_returns_false()
        {
            var probe = CreateTestProbe();
            var settings = NextSettings();

            Sys.ActorOf(
                ClusterSingletonManager.Props(Props.Create(() => new ImportantSingleton(probe.Ref)), PoisonPill.Instance, settings),
                settings.SingletonName);

            TestLease testLease = null;
            AwaitAssert(() =>
            {
                testLease = testLeaseExt.GetTestLease(LeaseNameFor(settings));
            }); // allow singleton manager to create the lease

            probe.ExpectNoMsg(shortDuration);
            testLease.InitialPromise.SetResult(false);
            probe.ExpectNoMsg(shortDuration);
        }

        [Fact]
        public void ClusterSingleton_with_lease_should_retry_trying_to_get_lease_if_acquire_returns_false()
        {
            var singletonProbe = CreateTestProbe();
            var settings = NextSettings();

            Sys.ActorOf(
                ClusterSingletonManager.Props(Props.Create(() => new ImportantSingleton(singletonProbe.Ref)), PoisonPill.Instance, settings),
                settings.SingletonName);

            TestLease testLease = null;
            AwaitAssert(() =>
            {
                testLease = testLeaseExt.GetTestLease(LeaseNameFor(settings));
            }); // allow singleton manager to create the lease

            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            singletonProbe.ExpectNoMsg(shortDuration);
            TaskCompletionSource<bool> nextResponse = new TaskCompletionSource<bool>();

            testLease.SetNextAcquireResult(nextResponse.Task);
            testLease.InitialPromise.SetResult(false);
            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            singletonProbe.ExpectNoMsg(shortDuration);
            nextResponse.SetResult(true);
            singletonProbe.ExpectMsg("preStart");
        }

        [Fact]
        public void ClusterSingleton_with_lease_should_do_not_start_if_lease_acquire_fails()
        {
            var probe = CreateTestProbe();
            var settings = NextSettings();

            Sys.ActorOf(
                ClusterSingletonManager.Props(Props.Create(() => new ImportantSingleton(probe.Ref)), PoisonPill.Instance, settings),
                settings.SingletonName);

            TestLease testLease = null;
            AwaitAssert(() =>
            {
                testLease = testLeaseExt.GetTestLease(LeaseNameFor(settings));
            }); // allow singleton manager to create the lease


            probe.ExpectNoMsg(shortDuration);
            testLease.InitialPromise.SetException(new TestException("no lease for you"));
            probe.ExpectNoMsg(shortDuration);
        }

        [Fact]
        public void ClusterSingleton_with_lease_should_retry_trying_to_get_lease_if_acquire_returns_fails()
        {
            var singletonProbe = CreateTestProbe();
            var settings = NextSettings();

            Sys.ActorOf(
                ClusterSingletonManager.Props(Props.Create(() => new ImportantSingleton(singletonProbe.Ref)), PoisonPill.Instance, settings),
                settings.SingletonName);

            TestLease testLease = null;
            AwaitAssert(() =>
            {
                testLease = testLeaseExt.GetTestLease(LeaseNameFor(settings));
            }); // allow singleton manager to create the lease

            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            singletonProbe.ExpectNoMsg(shortDuration);
            TaskCompletionSource<bool> nextResponse = new TaskCompletionSource<bool>();
            testLease.SetNextAcquireResult(nextResponse.Task);
            testLease.InitialPromise.SetException(new TestException("no lease for you"));
            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            singletonProbe.ExpectNoMsg(shortDuration);
            nextResponse.SetResult(true);
            singletonProbe.ExpectMsg("preStart");
        }

        [Fact]
        public void ClusterSingleton_with_lease_should_stop_singleton_if_the_lease_fails_periodic_check()
        {
            var lifecycleProbe = CreateTestProbe();
            var settings = NextSettings();

            Sys.ActorOf(
                ClusterSingletonManager.Props(Props.Create(() => new ImportantSingleton(lifecycleProbe.Ref)), PoisonPill.Instance, settings),
                settings.SingletonName);

            TestLease testLease = null;
            AwaitAssert(() =>
            {
                testLease = testLeaseExt.GetTestLease(LeaseNameFor(settings));
            }); // allow singleton manager to create the lease

            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            testLease.InitialPromise.SetResult(true);
            lifecycleProbe.ExpectMsg("preStart");
            var callback = testLease.GetCurrentCallback();
            callback(null);
            lifecycleProbe.ExpectMsg("postStop");
            testLease.Probe.ExpectMsg(new TestLease.ReleaseReq(leaseOwner));

            // should try and reacquire lease
            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            lifecycleProbe.ExpectMsg("preStart");
        }

        [Fact]
        public void ClusterSingleton_with_lease_should_release_lease_when_leaving_oldest()
        {
            var singletonProbe = CreateTestProbe();
            var settings = NextSettings();

            Sys.ActorOf(
                ClusterSingletonManager.Props(Props.Create(() => new ImportantSingleton(singletonProbe.Ref)), PoisonPill.Instance, settings),
                settings.SingletonName);

            TestLease testLease = null;
            AwaitAssert(() =>
            {
                testLease = testLeaseExt.GetTestLease(LeaseNameFor(settings));
            }); // allow singleton manager to create the lease

            singletonProbe.ExpectNoMsg(shortDuration);
            testLease.Probe.ExpectMsg(new TestLease.AcquireReq(leaseOwner));
            testLease.InitialPromise.SetResult(true);
            singletonProbe.ExpectMsg("preStart");
            cluster.Leave(cluster.SelfAddress);
            testLease.Probe.ExpectMsg(new TestLease.ReleaseReq(leaseOwner));
        }
    }
}
