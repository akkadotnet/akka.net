//------------------------------------------_-----------------------------
// <copyright file="DeadLetterSupressionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class DeadLetterSuspensionSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka.loglevel = INFO
            akka.log-dead-letters = 3
            akka.log-dead-letters-suspend-duration = 2s");

        private readonly IActorRef _deadActor;

        public DeadLetterSuspensionSpec()
            : base(Config)
        {
            _deadActor = Sys.ActorOf(Props.Create<TestKit.TestActors.EchoActor>());
            Watch(_deadActor);
            _deadActor.Tell(PoisonPill.Instance);
            ExpectTerminated(_deadActor);
        }

        private string ExpectedDeadLettersLogMessage(int count) =>
            $"Message [{count.GetType().Name}] from {TestActor.Path} to {_deadActor.Path} was not delivered. [{count}] dead letters encountered";

        [Fact]
        public void Must_suspend_dead_letters_logging_when_reaching_akka_log_dead_letters_and_then_re_enable()
        {
            EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(1))
                .Expect(1, () => _deadActor.Tell(1));

            EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(2))
                .Expect(1, () => _deadActor.Tell(2));

            EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(3) + ", no more dead letters will be logged in next")
                .Expect(1, () => _deadActor.Tell(3));

            _deadActor.Tell(4);
            _deadActor.Tell(5);

            // let suspend-duration elapse
            Thread.Sleep(2050);

            // re-enabled
            EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(6) + ", of which 2 were not logged")
                .Expect(1, () => _deadActor.Tell(6));

            // reset count
            EventFilter
                .Info(start: ExpectedDeadLettersLogMessage(1))
                .Expect(1, () => _deadActor.Tell(7));
        }
    }
}
