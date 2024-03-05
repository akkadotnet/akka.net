//-----------------------------------------------------------------------
// <copyright file="RemoteWatcherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests
{
    public class RemoteWatcherSpec : AkkaSpec
    {
        private class TestActorProxy : UntypedActor
        {
            private readonly IActorRef _testActor;

            public TestActorProxy(IActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override void OnReceive(object message)
            {
                _testActor.Forward(message);    
            }
        }

        private class MyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }

        public static readonly TimeSpan TurnOff = TimeSpan.FromMinutes(5);

        private static IFailureDetectorRegistry<Address> CreateFailureDetectorRegistry()
        {
            return new DefaultFailureDetectorRegistry<Address>(() => new PhiAccrualFailureDetector(
                8,
                200,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(1)));
        }

        private class TestRemoteWatcher : RemoteWatcher
        {
            public class AddressTerm
            {
                public AddressTerm(Address address)
                {
                    Address = address;
                }

                public Address Address { get; }

                public override bool Equals(object obj)
                {
                    if (!(obj is AddressTerm other)) return false;
                    return Address.Equals(other.Address);
                }

                public override int GetHashCode()
                {
                    return Address.GetHashCode();
                }
            }

            public class Quarantined
            {
                public Quarantined(Address address, int? uid)
                {
                    Address = address;
                    Uid = uid;
                }

                public Address Address { get; }

                public int? Uid { get; }

                protected bool Equals(Quarantined other)
                {
                    return Equals(Address, other.Address) && Uid == other.Uid;
                }

                public override bool Equals(object obj)
                {
                    if (ReferenceEquals(null, obj)) return false;
                    if (ReferenceEquals(this, obj)) return true;
                    if(!(obj is Quarantined q)) return false;
                    return Equals(q);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        return ((Address != null ? Address.GetHashCode() : 0)*397) ^ Uid.GetHashCode();
                    }
                }

                public static bool operator ==(Quarantined left, Quarantined right)
                {
                    return Equals(left, right);
                }

                public static bool operator !=(Quarantined left, Quarantined right)
                {
                    return !Equals(left, right);
                }
            }

            public TestRemoteWatcher(TimeSpan heartbeatExpectedResponseAfter) 
                : base(
                    CreateFailureDetectorRegistry(), 
                    TurnOff, 
                    TurnOff,
                    heartbeatExpectedResponseAfter)
            {   
            }

            public TestRemoteWatcher() : this(TurnOff)
            {
            }

            protected override void PublishAddressTerminated(Address address)
            {
                // don't publish the real AddressTerminated, but a testable message,
                // that doesn't interfere with the real watch that is going on in the background
                Context.System.EventStream.Publish(new AddressTerm(address));
            }

            protected override void Quarantine(Address address, int? addressUid)
            {
                // don't quarantine in remoting, but publish a testable message
                Context.System.EventStream.Publish(new Quarantined(address, addressUid));
            }
        }

        public RemoteWatcherSpec(ITestOutputHelper output)
            : base(@"
            akka {
                loglevel = INFO 
                log-dead-letters-during-shutdown = false
                actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                remote.dot-netty.tcp = {
                    hostname = localhost
                    port = 0
                }
            }", output)
        {
            _remoteSystem = ActorSystem.Create("RemoteSystem", Sys.Settings.Config);
            _remoteAddress = _remoteSystem.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            var remoteAddressUid = AddressUidExtension.Uid(_remoteSystem);

            //TODO: Mute dead letters?
            /*
            Seq(system, remoteSystem).foreach(muteDeadLetters(
                akka.remote.transport.AssociationHandle.Disassociated.getClass,
                akka.remote.transport.ActorTransportAdapter.DisassociateUnderlying.getClass)(_))
            */

            _heartbeatRspB = new RemoteWatcher.HeartbeatRsp(remoteAddressUid);
        }

        protected override void AfterAll()
        {
            Shutdown(_remoteSystem);
            base.AfterAll();
        }

        private readonly ActorSystem _remoteSystem;
        private readonly Address _remoteAddress;
        private readonly RemoteWatcher.HeartbeatRsp _heartbeatRspB;

        private int RemoteAddressUid
        {
            get { return AddressUidExtension.Uid(_remoteSystem); }
        }

        private async Task<IInternalActorRef> CreateRemoteActor(Props props, string name)
        {
            _remoteSystem.ActorOf(props, name);
            Sys.ActorSelection(new RootActorPath(_remoteAddress) / "user" / name).Tell(new Identify(name), TestActor);
            return (await ExpectMsgAsync<ActorIdentity>()).Subject.AsInstanceOf<IInternalActorRef>();
        }

        [Fact]
        public async Task A_RemoteWatcher_must_have_correct_interaction_when_watching()
        {
            var fd = CreateFailureDetectorRegistry();
            var monitorA = Sys.ActorOf(Props.Create<TestRemoteWatcher>(), "monitor1");
            //TODO: Better way to write this?
            var monitorB = await CreateRemoteActor(new Props(new Deploy(), typeof(TestActorProxy), TestActor), "monitor1");

            var a1 = Sys.ActorOf(Props.Create<MyActor>(), "a1").AsInstanceOf<IInternalActorRef>();
            var a2 = Sys.ActorOf(Props.Create<MyActor>(), "a2").AsInstanceOf<IInternalActorRef>();
            var b1 = await CreateRemoteActor(Props.Create<MyActor>(), "b1");
            var b2 = await CreateRemoteActor(Props.Create<MyActor>(), "b2");

            monitorA.Tell(new RemoteWatcher.WatchRemote(b1, a1));
            monitorA.Tell(new RemoteWatcher.WatchRemote(b2, a1));
            monitorA.Tell(new RemoteWatcher.WatchRemote(b2, a2));
            monitorA.Tell(RemoteWatcher.Stats.Empty, TestActor);
            // (a1->b1), (a1->b2), (a2->b2)
            await ExpectMsgAsync(RemoteWatcher.Stats.Counts(3, 1));
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            monitorA.Tell(_heartbeatRspB, monitorB);
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            monitorA.Tell(new RemoteWatcher.UnwatchRemote(b1, a1));
            // still (a1->b2) and (a2->b2) left
            monitorA.Tell(RemoteWatcher.Stats.Empty, TestActor);
            await ExpectMsgAsync(RemoteWatcher.Stats.Counts(2, 1));
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            monitorA.Tell(new RemoteWatcher.UnwatchRemote(b2, a2));
            // still (a1->b2) left
            monitorA.Tell(RemoteWatcher.Stats.Empty, TestActor);
            await ExpectMsgAsync(RemoteWatcher.Stats.Counts(1, 1));
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            monitorA.Tell(new RemoteWatcher.UnwatchRemote(b2, a1));
            // all unwatched
            monitorA.Tell(RemoteWatcher.Stats.Empty, TestActor);
            await ExpectMsgAsync(RemoteWatcher.Stats.Empty);
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

            // make sure nothing floods over to next test
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task A_RemoteWatcher_must_generate_address_terminated_when_missing_heartbeats()
        {
            var p = CreateTestProbe();
            var q = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof (TestRemoteWatcher.AddressTerm));
            Sys.EventStream.Subscribe(q.Ref, typeof(TestRemoteWatcher.Quarantined));

            var monitorA = Sys.ActorOf(Props.Create<TestRemoteWatcher>(), "monitor4");
            var monitorB = await CreateRemoteActor(new Props(new Deploy(), typeof(TestActorProxy), TestActor), "monitor4");

            var a = Sys.ActorOf(Props.Create<MyActor>(), "a4").AsInstanceOf<IInternalActorRef>();
            var b = await CreateRemoteActor(Props.Create<MyActor>(), "b4");

            monitorA.Tell(new RemoteWatcher.WatchRemote(b, a));

            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(_heartbeatRspB, monitorB);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(_heartbeatRspB, monitorB);

            await WithinAsync(10.Seconds(), async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
                    await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
                    //but no HeartbeatRsp
                    monitorA.Tell(RemoteWatcher.ReapUnreachableTick.Instance);
                    await p.ExpectMsgAsync(new TestRemoteWatcher.AddressTerm(b.Path.Address), TimeSpan.FromSeconds(1));
                    await q.ExpectMsgAsync(new TestRemoteWatcher.Quarantined(b.Path.Address, RemoteAddressUid), TimeSpan.FromSeconds(1));
                });
                return true;
            });

            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task A_RemoteWatcher_must_generate_address_terminated_when_missing_first_heartbeat()
        {
            var p = CreateTestProbe();
            var q = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof (TestRemoteWatcher.AddressTerm));
            Sys.EventStream.Subscribe(q.Ref, typeof (TestRemoteWatcher.Quarantined));

            var fd = CreateFailureDetectorRegistry();
            var heartbeatExpectedResponseAfter = TimeSpan.FromSeconds(2);
            var monitorA = Sys.ActorOf(new Props(new Deploy(), typeof(TestRemoteWatcher), heartbeatExpectedResponseAfter), "monitor5");
            var monitorB = await CreateRemoteActor(new Props(new Deploy(), typeof(TestActorProxy), TestActor), "monitor5");

            var a = Sys.ActorOf(Props.Create<MyActor>(), "a5").AsInstanceOf<IInternalActorRef>();
            var b = await CreateRemoteActor(Props.Create<MyActor>(), "b5");

            monitorA.Tell(new RemoteWatcher.WatchRemote(b, a));

            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            // no HeartbeatRsp sent

            await WithinAsync(20.Seconds(), async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
                    await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
                    //but no HeartbeatRsp
                    monitorA.Tell(RemoteWatcher.ReapUnreachableTick.Instance);
                    await p.ExpectMsgAsync(new TestRemoteWatcher.AddressTerm(b.Path.Address), TimeSpan.FromSeconds(1));
                    // no real quarantine when missing first heartbeat, uid unknown
                    await q.ExpectMsgAsync(new TestRemoteWatcher.Quarantined(b.Path.Address, null), TimeSpan.FromSeconds(1));
                });
                return true;
            });

            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task
            A_RemoteWatcher_must_generate_address_terminated_for_new_watch_after_broken_connection_was_reestablished_and_broken_again()
        {
            var p = CreateTestProbe();
            var q = CreateTestProbe();
            Sys.EventStream.Subscribe(p.Ref, typeof(TestRemoteWatcher.AddressTerm));
            Sys.EventStream.Subscribe(q.Ref, typeof(TestRemoteWatcher.Quarantined));

            var monitorA = Sys.ActorOf(Props.Create<TestRemoteWatcher>(), "monitor6");
            var monitorB = await CreateRemoteActor(new Props(new Deploy(), typeof(TestActorProxy), new[] { TestActor }), "monitor6");

            var a = Sys.ActorOf(Props.Create<MyActor>(), "a6").AsInstanceOf<IInternalActorRef>();
            var b = await CreateRemoteActor(Props.Create<MyActor>(), "b6");

            monitorA.Tell(new RemoteWatcher.WatchRemote(b, a));

            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(_heartbeatRspB, monitorB);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(_heartbeatRspB, monitorB);

            await WithinAsync(10.Seconds(), async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
                    await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
                    //but no HeartbeatRsp
                    monitorA.Tell(RemoteWatcher.ReapUnreachableTick.Instance);
                    await p.ExpectMsgAsync(new TestRemoteWatcher.AddressTerm(b.Path.Address), TimeSpan.FromSeconds(1));
                    await q.ExpectMsgAsync(new TestRemoteWatcher.Quarantined(b.Path.Address, RemoteAddressUid),
                        TimeSpan.FromSeconds(1));
                });
                return true;
            });

            //real AddressTerminated would trigger Terminated for b6, simulate that here
            _remoteSystem.Stop(b);
            await AwaitAssertAsync(async () =>
            {
                monitorA.Tell(RemoteWatcher.Stats.Empty, TestActor);
                await ExpectMsgAsync(RemoteWatcher.Stats.Empty);
            });
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));

            //assume that connection comes up again, or remote system is restarted
            var c = await CreateRemoteActor(Props.Create<MyActor>(), "c6");
            monitorA.Tell(new RemoteWatcher.WatchRemote(c,a));

            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(_heartbeatRspB, monitorB);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(_heartbeatRspB, monitorB);
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(RemoteWatcher.ReapUnreachableTick.Instance, TestActor);
            await p.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(_heartbeatRspB, monitorB);
            monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
            await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
            monitorA.Tell(RemoteWatcher.ReapUnreachableTick.Instance, TestActor);
            await p.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            await q.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

            //then stop heartbeating again; should generate a new AddressTerminated
            await WithinAsync(10.Seconds(), async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    monitorA.Tell(RemoteWatcher.HeartbeatTick.Instance, TestActor);
                    await ExpectMsgAsync<RemoteWatcher.Heartbeat>();
                    //but no HeartbeatRsp
                    monitorA.Tell(RemoteWatcher.ReapUnreachableTick.Instance);
                    await p.ExpectMsgAsync(new TestRemoteWatcher.AddressTerm(b.Path.Address), TimeSpan.FromSeconds(1));
                    await q.ExpectMsgAsync(new TestRemoteWatcher.Quarantined(b.Path.Address, RemoteAddressUid), TimeSpan.FromSeconds(1));
                });
                return true;
            });

            //make sure nothing floods over to next test
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
        }

    }
}

