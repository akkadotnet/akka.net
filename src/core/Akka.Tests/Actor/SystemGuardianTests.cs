//-----------------------------------------------------------------------
// <copyright file="SystemGuardianTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor
{
    public class SystemGuardianTests : AkkaSpec
    {
        readonly IInternalActorRef _userGuardian;
        readonly IInternalActorRef _systemGuardian;

        public SystemGuardianTests()
        {
            _userGuardian = Sys.ActorOf(Props.Create<GuardianActor>()).AsInstanceOf<IInternalActorRef>();
            _systemGuardian = Sys.ActorOf(Props.Create(() => new SystemGuardianActor(_userGuardian))).AsInstanceOf<IInternalActorRef>();
            _systemGuardian.SendSystemMessage(new Watch(_userGuardian, _systemGuardian));            
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
            var guardianWatcher = CreateTestProbe();
            guardianWatcher.Watch(_systemGuardian);

            var registree = CreateTestProbe();
            registree.Send(_systemGuardian, RegisterTerminationHook.Instance);
            registree.Tell(PoisonPill.Instance);

            _userGuardian.Tell(PoisonPill.Instance);

            guardianWatcher.ExpectTerminated(_systemGuardian);
        }
    }
}

