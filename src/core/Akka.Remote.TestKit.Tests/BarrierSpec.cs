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
                    return ((_ref != null ? _ref.GetHashCode() : 0) * 397) ^
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

        readonly RoleName A = new RoleName("a");
        readonly RoleName B = new RoleName("b");
        readonly RoleName C = new RoleName("c");

        [Fact]
        public void ABarrierCoordinatorMustRegisterClientsAndRemoveThem()
        {
            var b = GetBarrier();
            b.Tell(new Controller.NodeInfo(A, Address.Parse("akka://sys"), Sys.DeadLetters));
            b.Tell(new Controller.ClientDisconnected(B));
            b.Tell(new Controller.ClientDisconnected(A));
            //TODO: ErrorFilter?
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

        private ActorRef GetBarrier()
        {
            var actor = Sys.ActorOf(new Props(typeof (BarrierCoordinatorSupervisor), new object[] {TestActor}).WithDeploy(Deploy.Local));
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

        public ActorRef Self { get { return TestActor; } }
    }
}