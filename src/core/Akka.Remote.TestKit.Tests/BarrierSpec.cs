//-----------------------------------------------------------------------
// <copyright file="BarrierSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.TestKit.Tests
{
    public class BarrierSpec : AkkaSpec
    {
        private sealed class Failed
        {
            private readonly IActorRef _ref;
            private readonly Exception _exception;

            public Failed(IActorRef @ref, Exception exception)
            {
                _ref = @ref;
                _exception = exception;
            }

            public IActorRef Ref
            {
                get { return _ref; }
            }

            public Exception Exception
            {
                get { return _exception; }
            }

            private bool Equals(Failed other)
            {
                return Equals(_ref, other._ref) && _exception.GetType() == other._exception.GetType();
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is Failed failed && Equals(failed);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_ref != null ? _ref.GetHashCode() : 0)*397) ^
                           (_exception != null ? _exception.GetHashCode() : 0);
                }
            }

            public static bool operator ==(Failed left, Failed right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(Failed left, Failed right)
            {
                return !Equals(left, right);
            }
        }

        private const string Config = @"
            akka.testconductor.barrier-timeout = 5s
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.actor.debug.fsm = on
            akka.actor.debug.lifecycle = on
        ";

        public BarrierSpec() : base(Config)
        {
        }

        private readonly RoleName A = new("a");
        private readonly RoleName B = new("b");
        private readonly RoleName C = new("c");

        [Fact]
        public void A_BarrierCoordinator_must_register_clients_and_remove_them()
        {
            var b = GetBarrier();
            b.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), Sys.DeadLetters), TestActor);
            b.Tell(new BarrierCoordinator.RemoveClient(B));
            b.Tell(new BarrierCoordinator.RemoveClient(A));
            //EventFilter<BarrierCoordinator.BarrierEmpty>(1, () => b.Tell(new BarrierCoordinator.RemoveClient(A), TestActor)); //appears to be a bug in the testfilter
            b.Tell(new BarrierCoordinator.RemoveClient(A));
            ExpectMsg(new Failed(b,
                new BarrierCoordinator.BarrierEmptyException(
                    new BarrierCoordinator.Data(ImmutableHashSet.Create<Controller.NodeInfo>(), "", null, null),
                    "cannot remove RoleName(a): no client to remove")));
        }

        [Fact]
        public void A_BarrierCoordinator_must_register_clients_and_disconnect_them()
        {
            var b = GetBarrier();
            b.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), Sys.DeadLetters));
            b.Tell(new Controller.ClientDisconnected(B));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            b.Tell(new Controller.ClientDisconnected(A));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task A_BarrierCoordinator_must_fail_entering_barrier_when_nobody_registered()
        {
            var b = GetBarrier();
            b.Tell(new EnterBarrier("bar1", null, new RoleName("b")), TestActor);
            await ExpectMsgAsync(new ToClient<BarrierResult>(new BarrierResult("bar1", false)), TimeSpan.FromSeconds(300));
        }

        [Fact]
        public async Task A_BarrierCoordinator_must_enter_barrier()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            a.Send(barrier, new EnterBarrier("bar2", null, new RoleName("a")));
            NoMsg(a, b);
            await WithinAsync(TimeSpan.FromSeconds(2), async () =>
            {
                b.Send(barrier, new EnterBarrier("bar2", null, new RoleName("b")));
                await a.ExpectMsgAsync(new ToClient<BarrierResult>(new BarrierResult("bar2", true)));
                await b.ExpectMsgAsync(new ToClient<BarrierResult>(new BarrierResult("bar2", true)));
            });
        }

        [Fact]
        public async Task A_BarrierCoordinator_must_enter_barrier_with_joining_node()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var c = CreateTestProbe();
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            a.Send(barrier, new EnterBarrier("bar3", null, new RoleName("a")));
            barrier.Tell(new Controller.NodeInfo(C, Address.Parse("akka://sys"), c.Ref));
            b.Send(barrier, new EnterBarrier("bar3", null, new RoleName("b")));
            NoMsg(a, b, c);
            await WithinAsync(TimeSpan.FromSeconds(2), async () =>
            {
                c.Send(barrier, new EnterBarrier("bar3", null, new RoleName("c")));
                await a.ExpectMsgAsync(new ToClient<BarrierResult>(new BarrierResult("bar3", true)));
                await b.ExpectMsgAsync(new ToClient<BarrierResult>(new BarrierResult("bar3", true)));
                await c.ExpectMsgAsync(new ToClient<BarrierResult>(new BarrierResult("bar3", true)));
            });
        }

        [Fact]
        public async Task A_BarrierCoordinator_must_enter_barrier_with_leaving_node()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var c = CreateTestProbe();
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            barrier.Tell(new Controller.NodeInfo(C, Address.Parse("akka://sys"), c.Ref));
            a.Send(barrier, new EnterBarrier("bar4", null, new RoleName("a")));
            b.Send(barrier, new EnterBarrier("bar4", null, new RoleName("b")));
            barrier.Tell(new BarrierCoordinator.RemoveClient(A));
            barrier.Tell(new Controller.ClientDisconnected(A));
            NoMsg(a, b, c);
            await WithinAsync(TimeSpan.FromSeconds(2), async () =>
            {
                barrier.Tell(new BarrierCoordinator.RemoveClient(C));
                await b.ExpectMsgAsync(new ToClient<BarrierResult>(new BarrierResult("bar4", true)));
            });
            barrier.Tell(new Controller.ClientDisconnected(C));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task A_BarrierCoordinator_must_enter_leave_barrier_when_last_arrived_is_removed()
        {
            var barrier = GetBarrier();
            var roleName = new RoleName("normal");
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            a.Send(barrier, new EnterBarrier("bar5", null, roleName));
            barrier.Tell(new BarrierCoordinator.RemoveClient(A));
            b.Send(barrier, new EnterBarrier("foo", null, roleName));
            await b.ExpectMsgAsync(new ToClient<BarrierResult>(new BarrierResult("foo", true)));
        }

        [Fact]
        public async Task A_BarrierCoordinator_must_fail_barrier_with_disconnecting_node()
        {
            var barrier = GetBarrier();
            var roleName = new RoleName("normal");
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var nodeA = new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref);
            barrier.Tell(nodeA);
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            a.Send(barrier, new EnterBarrier("bar6", null, roleName));
            //TODO: EventFilter?
            barrier.Tell(new Controller.ClientDisconnected(B));
            var msg = await ExpectMsgAsync<Failed>();
            Assert.Equal(new BarrierCoordinator.ClientLostException(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA),
                    "bar6", 
                    ImmutableHashSet.Create(a.Ref),
                    ((BarrierCoordinator.ClientLostException) msg.Exception).BarrierData.Deadline)
                , B), msg.Exception);
        }

        [Fact]
        public async Task A_BarrierCoordinator_must_fail_barrier_when_disconnecting_node_who_already_arrived()
        {
            var barrier = GetBarrier();
            var roleNameA = new RoleName("a");
            var roleNameB = new RoleName("b");
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var c = CreateTestProbe();
            var nodeA = new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref);
            var nodeC = new Controller.NodeInfo(C, Address.Parse("akka://sys"), c.Ref);
            barrier.Tell(nodeA);
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            barrier.Tell(nodeC);
            a.Send(barrier, new EnterBarrier("bar7", null, roleNameA));
            b.Send(barrier, new EnterBarrier("bar7", null, roleNameB));
            //TODO: Event filter?
            barrier.Tell(new Controller.ClientDisconnected(B));
            var msg = await ExpectMsgAsync<Failed>();
            Assert.Equal(new BarrierCoordinator.ClientLostException(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA, nodeC),
                    "bar7", 
                    ImmutableHashSet.Create(a.Ref),
                    ((BarrierCoordinator.ClientLostException)msg.Exception).BarrierData.Deadline)
                , B), msg.Exception);

        }

        [Fact]
        public async Task A_BarrierCoordinator_must_fail_when_entering_wrong_barrier()
        {
            var barrier = GetBarrier();
            var roleName = new RoleName("failer");
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var nodeA = (new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(nodeA);
            var nodeB = (new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            barrier.Tell(nodeB);
            a.Send(barrier, new EnterBarrier("bar8", null, roleName));
            //TODO: Event filter
            b.Send(barrier, new EnterBarrier("foo", null, roleName));
            var msg = await ExpectMsgAsync<Failed>();
            Assert.Equal(new BarrierCoordinator.WrongBarrierException(
                "foo",
                b.Ref,
                roleName,
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA, nodeB),
                    "bar8",
                    ImmutableHashSet.Create(a.Ref),
                    ((BarrierCoordinator.WrongBarrierException)msg.Exception).BarrierData.Deadline)
                ), msg.Exception);
        }
        
        [Fact]
        public async Task A_BarrierCoordinator_must_fail_barrier_after_first_failure()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var roleName = new RoleName("failer");
            //TODO: EventFilter
            barrier.Tell(new BarrierCoordinator.RemoveClient(A));
            var msg = await ExpectMsgAsync<Failed>();
            Assert.Equal(new BarrierCoordinator.BarrierEmptyException(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create<Controller.NodeInfo>(),
                    "",
                    ImmutableHashSet.Create<IActorRef>(),
                    ((BarrierCoordinator.BarrierEmptyException)msg.Exception).BarrierData.Deadline)
                , "cannot remove RoleName(a): no client to remove"), msg.Exception);
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            a.Send(barrier, new EnterBarrier("bar9", null, roleName));
            a.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar9", false)));
        }

        [Fact]
        public async Task A_BarrierCoordinator_must_fail_after_barrier_timeout()
        {
            var barrier = GetBarrier();
            var roleName = new RoleName("failer");
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var nodeA = new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref);
            var nodeB = new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref);
            barrier.Tell(nodeA);
            barrier.Tell(nodeB);
            a.Send(barrier, new EnterBarrier("bar10", null, roleName));
            await EventFilter.Exception<BarrierCoordinator.BarrierTimeoutException>().ExpectOneAsync(async () =>
            {
                var msg = await ExpectMsgAsync<Failed>(TimeSpan.FromSeconds(7));
                Assert.Equal(new BarrierCoordinator.BarrierTimeoutException(
                    new BarrierCoordinator.Data(
                        ImmutableHashSet.Create(nodeA, nodeB),
                        "bar10",
                        ImmutableHashSet.Create(a.Ref),
                        ((BarrierCoordinator.BarrierTimeoutException)msg.Exception).BarrierData.Deadline)
                    ), msg.Exception);
            });
        }

        [Fact]
        public void A_BarrierCoordinator_must_fail_if_a_node_registers_twice()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var nodeA = new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref);
            var nodeB = new Controller.NodeInfo(A, Address.Parse("akka://sys"), b.Ref);
            barrier.Tell(nodeA);
            //TODO: Event filter
            barrier.Tell(nodeB);
            var msg = ExpectMsg<Failed>();
            Assert.Equal(new BarrierCoordinator.DuplicateNodeException(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA),
                    "",
                    ImmutableHashSet.Create<IActorRef>(),
                    ((BarrierCoordinator.DuplicateNodeException)msg.Exception).BarrierData.Deadline)
                , nodeB), msg.Exception);
        }

        //TODO: Controller tests.

        private IActorRef GetBarrier()
        {
            var actor =
                Sys.ActorOf(
                    new Props(typeof (BarrierCoordinatorSupervisor), new object[] {TestActor}).WithDeploy(Deploy.Local));
            actor.Tell("", TestActor);
            return ExpectMsg<IActorRef>();
        }

        private class BarrierCoordinatorSupervisor : UntypedActor
        {
            private readonly IActorRef _testActor;
            private readonly IActorRef _barrier;

            public BarrierCoordinatorSupervisor(IActorRef testActor)
            {
                _testActor = testActor;
                _barrier = Context.ActorOf(Props.Create<BarrierCoordinator>());
            }

            protected override void OnReceive(object message)
            {
                Sender.Tell(_barrier);
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(e =>
                {
                    _testActor.Tell(new Failed(_barrier, e));
                    return Directive.Restart;
                });
            }
        }

        private void NoMsg(params TestProbe[] probes)
        {
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            foreach (var probe in probes) Assert.False(probe.HasMessages);
        }
    }
}

