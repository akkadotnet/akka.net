using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.TestKit.Tests
{
    public class BarrierSpec : AkkaSpec, ImplicitSender
    {
        private sealed class Failed
        {
            private readonly ActorRef _ref;
            private readonly Exception _exception;

            public Failed(ActorRef @ref, Exception exception)
            {
                _ref = @ref;
                _exception = exception;
            }

            public ActorRef Ref
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
                return obj is Failed && Equals((Failed) obj);
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

        private readonly RoleName A = new RoleName("a");
        private readonly RoleName B = new RoleName("b");
        private readonly RoleName C = new RoleName("c");

        [Fact]
        public void ABarrierCoordinatorMustRegisterClientsAndRemoveThem()
        {
            var b = GetBarrier();
            b.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), Sys.DeadLetters), TestActor);
            b.Tell(new BarrierCoordinator.RemoveClient(B));
            b.Tell(new BarrierCoordinator.RemoveClient(A));
            //EventFilter<BarrierCoordinator.BarrierEmpty>(1, () => b.Tell(new BarrierCoordinator.RemoveClient(A), TestActor)); //appears to be a bug in the testfilter
            b.Tell(new BarrierCoordinator.RemoveClient(A));
            ExpectMsg(new Failed(b,
                new BarrierCoordinator.BarrierEmpty(
                    new BarrierCoordinator.Data(ImmutableHashSet.Create<Controller.NodeInfo>(), "", null, null),
                    "cannot remove RoleName(a): no client to remove")));
        }

        [Fact]
        public void ABarrierCoordinatorMustRegisterClientsAndDisconnectThem()
        {
            var b = GetBarrier();
            b.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), Sys.DeadLetters));
            b.Tell(new Controller.ClientDisconnected(B));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
            b.Tell(new Controller.ClientDisconnected(A));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void ABarrierCoordinatorMustFailEnteringBarrierWhenNobodyRegistered()
        {
            var b = GetBarrier();
            b.Tell(new EnterBarrier("bar1", null), TestActor);
            ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar1", false)), TimeSpan.FromSeconds(300));
        }

        [Fact]
        public void ABarrierCoordinatorMustEnterBarrier()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            a.Send(barrier, new EnterBarrier("bar2", null));
            NoMsg(a, b);
            Within(TimeSpan.FromSeconds(2), () =>
            {
                b.Send(barrier, new EnterBarrier("bar2", null));
                a.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar2", true)));
                b.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar2", true)));
            });
        }

        [Fact]
        public void ABarrierCoordinatorMustEnterBarrierWithJoiningNode()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var c = CreateTestProbe();
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            a.Send(barrier, new EnterBarrier("bar3", null));
            barrier.Tell(new Controller.NodeInfo(C, Address.Parse("akka://sys"), c.Ref));
            b.Send(barrier, new EnterBarrier("bar3", null));
            NoMsg(a, b, c);
            Within(TimeSpan.FromSeconds(2), () =>
            {
                c.Send(barrier, new EnterBarrier("bar3", null));
                a.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar3", true)));
                b.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar3", true)));
                c.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar3", true)));
            });
        }

        [Fact]
        public void ABarrierCoordinatorMustEnterBarrierWithLeavingNode()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var c = CreateTestProbe();
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            barrier.Tell(new Controller.NodeInfo(C, Address.Parse("akka://sys"), c.Ref));
            a.Send(barrier, new EnterBarrier("bar4", null));
            b.Send(barrier, new EnterBarrier("bar4", null));
            barrier.Tell(new BarrierCoordinator.RemoveClient(A));
            barrier.Tell(new Controller.ClientDisconnected(A));
            NoMsg(a, b, c);
            Within(TimeSpan.FromSeconds(2), () =>
            {
                barrier.Tell(new BarrierCoordinator.RemoveClient(C));
                b.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar4", true)));
            });
            barrier.Tell(new Controller.ClientDisconnected(C));
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void ABarrierCoordinatorMustEnterLeaveBarrierWhenLastArrivedIsRemoved()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            a.Send(barrier, new EnterBarrier("bar5", null));
            barrier.Tell(new BarrierCoordinator.RemoveClient(A));
            b.Send(barrier, new EnterBarrier("foo", null));
            b.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("foo", true)));
        }

        [Fact]
        public void ABarrierCoordinatorMustFailBarrierWithDisconnectingNode()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var nodeA = new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref);
            barrier.Tell(nodeA);
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            a.Send(barrier, new EnterBarrier("bar6", null));
            //TODO: EventFilter?
            barrier.Tell(new Controller.ClientDisconnected(B));
            var msg = ExpectMsg<Failed>();
            Assert.Equal(new BarrierCoordinator.ClientLost(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA),
                    "bar6", 
                    ImmutableHashSet.Create(a.Ref),
                    ((BarrierCoordinator.ClientLost) msg.Exception).BarrierData.Deadline)
                , B), msg.Exception);
        }

        [Fact]
        public void ABarrierCoordinatorMustFailBarrierWhenDisconnectingNodeWhoAlreadyArrived()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var c = CreateTestProbe();
            var nodeA = new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref);
            var nodeC = new Controller.NodeInfo(C, Address.Parse("akka://sys"), c.Ref);
            barrier.Tell(nodeA);
            barrier.Tell(new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            barrier.Tell(nodeC);
            a.Send(barrier, new EnterBarrier("bar7", null));
            b.Send(barrier, new EnterBarrier("bar7", null));
            //TODO: Event filter?
            barrier.Tell(new Controller.ClientDisconnected(B));
            var msg = ExpectMsg<Failed>();
            Assert.Equal(new BarrierCoordinator.ClientLost(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA, nodeC),
                    "bar7", 
                    ImmutableHashSet.Create(a.Ref),
                    ((BarrierCoordinator.ClientLost)msg.Exception).BarrierData.Deadline)
                , B), msg.Exception);

        }

        [Fact]
        public void ABarrierCoordinatorMustFailWhenEnteringWrongBarrier()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var nodeA = (new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            barrier.Tell(nodeA);
            var nodeB = (new Controller.NodeInfo(B, Address.Parse("akka://sys"), b.Ref));
            barrier.Tell(nodeB);
            a.Send(barrier, new EnterBarrier("bar8", null));
            //TODO: Event filter
            b.Send(barrier, new EnterBarrier("foo", null));
            var msg = ExpectMsg<Failed>();
            Assert.Equal(new BarrierCoordinator.WrongBarrier(
                "foo",
                b.Ref,
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA, nodeB),
                    "bar8",
                    ImmutableHashSet.Create(a.Ref),
                    ((BarrierCoordinator.WrongBarrier)msg.Exception).BarrierData.Deadline)
                ), msg.Exception);
        }
        
        [Fact]
        public void ABarrierCoordinatorMustFailBarrierAfterFirstFailure()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            //TODO: EventFilter
            barrier.Tell(new BarrierCoordinator.RemoveClient(A));
            var msg = ExpectMsg<Failed>();
            Assert.Equal(new BarrierCoordinator.BarrierEmpty(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create<Controller.NodeInfo>(),
                    "",
                    ImmutableHashSet.Create<ActorRef>(),
                    ((BarrierCoordinator.BarrierEmpty)msg.Exception).BarrierData.Deadline)
                , "cannot remove RoleName(a): no client to remove"), msg.Exception);
            barrier.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref));
            a.Send(barrier, new EnterBarrier("bar9", null));
            a.ExpectMsg(new ToClient<BarrierResult>(new BarrierResult("bar9", false)));
        }

        [Fact]
        public void ABarrierCoordinatorMustFailAfterBarrierTimeout()
        {
            var barrier = GetBarrier();
            var a = CreateTestProbe();
            var b = CreateTestProbe();
            var nodeA = new Controller.NodeInfo(A, Address.Parse("akka://sys"), a.Ref);
            var nodeB = new Controller.NodeInfo(A, Address.Parse("akka://sys"), b.Ref);
            barrier.Tell(nodeA);
            barrier.Tell(nodeB);
            a.Send(barrier, new EnterBarrier("bar10", null));
            //TODO: EventFilter
            //ExpectMsg<BarrierCoordinator.DuplicateNode>();
            var msg = ExpectMsg<Failed>(TimeSpan.FromSeconds(7));
            Assert.Equal(new BarrierCoordinator.BarrierTimeout(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA, nodeB),
                    "bar10",
                    ImmutableHashSet.Create(a.Ref),
                    ((BarrierCoordinator.BarrierTimeout)msg.Exception).BarrierData.Deadline)
                ), msg.Exception);
        }

        [Fact]
        public void ABarrierCoordinatorMustFailIfANodeRegistersTwice()
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
            Assert.Equal(new BarrierCoordinator.DuplicateNode(
                new BarrierCoordinator.Data(
                    ImmutableHashSet.Create(nodeA),
                    "",
                    ImmutableHashSet.Create<ActorRef>(),
                    ((BarrierCoordinator.DuplicateNode)msg.Exception).BarrierData.Deadline)
                , nodeB), msg.Exception);
        }

        //TODO: Controller tests.

        private ActorRef GetBarrier()
        {
            var actor =
                Sys.ActorOf(
                    new Props(typeof (BarrierCoordinatorSupervisor), new object[] {TestActor}).WithDeploy(Deploy.Local));
            actor.Tell("", TestActor);
            return ExpectMsg<ActorRef>();
        }

        private class BarrierCoordinatorSupervisor : UntypedActor
        {
            readonly ActorRef _testActor;
            readonly ActorRef _barrier;

            public BarrierCoordinatorSupervisor(ActorRef testActor)
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

        public ActorRef Self
        {
            get { return TestActor; }
        }
    }
}
