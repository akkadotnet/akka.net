using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class SystemGuardianTests : AkkaSpec
    {
        readonly IActorRef _userGuardian;
        readonly IActorRef _systemGuardian;

        public SystemGuardianTests()
        {
            _userGuardian = Sys.ActorOf(Props.Create<GuardianActor>());
            _systemGuardian = Sys.ActorOf(Props.Create(() => new SystemGuardianActor(_userGuardian)));
            _systemGuardian.Tell(new Watch(_userGuardian, _systemGuardian));            
        }

        [Fact]
        public void Should_Send_Hook_When_UserGuardian_Terminated()
        {
            _systemGuardian.Tell(RegisterTerminationHook.Instance);
            _userGuardian.Tell(PoisonPill.Instance);
            
            ExpectMsg<TerminationHook>();
        }

        [Fact]
        public void Should_Terminate_When_Hooks_Complete()
        {
            var probe = CreateTestProbe();
            probe.Watch(_systemGuardian);
            _systemGuardian.Tell(RegisterTerminationHook.Instance);
            _userGuardian.Tell(PoisonPill.Instance);

            ExpectMsg<TerminationHook>();
            _systemGuardian.Tell(TerminationHookDone.Instance);
            probe.ExpectTerminated(_systemGuardian);
        }

        [Fact]
        public void Should_Remove_Registration_When_Registree_Terminates()
        {
            var probe = CreateTestProbe();
            probe.Watch(_systemGuardian);
            _systemGuardian.Tell(RegisterTerminationHook.Instance);
            TestActor.Tell(PoisonPill.Instance);
            _userGuardian.Tell(PoisonPill.Instance);

            probe.ExpectTerminated(_systemGuardian);
        }
    }
}
