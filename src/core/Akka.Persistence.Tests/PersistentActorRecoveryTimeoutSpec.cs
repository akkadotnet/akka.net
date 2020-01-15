//-----------------------------------------------------------------------
// <copyright file="PersistentActorRecoveryTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Tests.Journal;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Persistence.Tests
{
    [Collection("PersistentActorRecoveryTimeout")] // force tests to run sequentially
    public class PersistentActorRecoveryTimeoutSpec : PersistenceSpec
    {
        private static readonly AtomicCounter JournalIdNumber = new AtomicCounter(0);
        private static readonly string JournalId = "persistent-actor-recovery-timeout-spec" + JournalIdNumber.GetAndIncrement();
        private readonly IActorRef _journal;

        #region internal test classes

        internal class RecoveryTimeoutActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public RecoveryTimeoutActor(IActorRef probe) : base("recovery-timeout-actor")
            {
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                Persist(message, x => Sender.Tell(message));
                return true;
            }

            protected override void OnRecoveryFailure(Exception reason, object message = null)
            {
                _probe.Tell(new Status.Failure(reason));
            }
        }

        internal class ReceiveTimeoutActor : NamedPersistentActor
        {
            private readonly TimeSpan _receiveTimeout;
            private readonly IActorRef _probe;
            private readonly ILoggingAdapter log = Context.GetLogger();

            public ReceiveTimeoutActor(TimeSpan receiveTimeout, IActorRef probe) : base("recovery-timeout-actor-2")
            {
                _receiveTimeout = receiveTimeout;
                _probe = probe;
            }

            protected override void PreStart()
            {
                base.PreStart();
                Context.SetReceiveTimeout(_receiveTimeout);
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is RecoveryCompleted)
                    _probe.Tell(Context.ReceiveTimeout);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                Persist(message, x => Sender.Tell(message));
                return true;
            }

            protected override void OnRecoveryFailure(Exception reason, object message = null)
            {
                log.Error("Recovery of ReceiveTimeoutActor failed");
                _probe.Tell(new Status.Failure(reason));
            }
        }

        #endregion

        public PersistentActorRecoveryTimeoutSpec()
            : base(SteppingMemoryJournal.Config(JournalId).WithFallback(
                ConfigurationFactory.ParseString(
                    @"akka.persistence.journal.stepping-inmem.recovery-event-timeout = 1s
                    akka.actor.serialize-messages = off"))
                  .WithFallback(Configuration("PersistentActorRecoveryTimeoutSpec")))
        {
            // initialize journal early
            Persistence.Instance.Apply(Sys).JournalFor("akka.persistence.journal.stepping-inmem");
            AwaitAssert(() => SteppingMemoryJournal.GetRef(JournalId), TimeSpan.FromSeconds(3));
            _journal = SteppingMemoryJournal.GetRef(JournalId);
        }

        [Fact]
        public void The_recovery_timeout_should_fail_recovery_if_timeout_is_not_met_when_recovering()
        {
            var probe = CreateTestProbe();
            var persisting = Sys.ActorOf(Props.Create(() => new RecoveryTimeoutActor(probe.Ref)));

            // initial read highest
            SteppingMemoryJournal.Step(_journal);

            persisting.Tell("A");
            SteppingMemoryJournal.Step(_journal);
            ExpectMsg("A");

            Watch(persisting);
            Sys.Stop(persisting);
            ExpectTerminated(persisting);

            // now replay, but don't give the journal any tokens to replay events
            // so that we cause the timeout to trigger
            var replaying = Sys.ActorOf(Props.Create(() => new RecoveryTimeoutActor(probe.Ref)));
            Watch(replaying);

            // initial read highest
            SteppingMemoryJournal.Step(_journal);

            probe.ExpectMsg<Status.Failure>().Cause.GetType().ShouldBe(typeof(RecoveryTimedOutException));
            ExpectTerminated(replaying);
        }

        [Fact]
        public void The_recovery_timeout_should_not_interfere_with_receive_timeout()
        {
            var timeout = TimeSpan.FromDays(42);

            var probe = CreateTestProbe();
            var persisting = Sys.ActorOf(Props.Create(() => new ReceiveTimeoutActor(timeout, probe.Ref)));

            // initial read highest
            SteppingMemoryJournal.Step(_journal);

            persisting.Tell("A");
            SteppingMemoryJournal.Step(_journal);
            ExpectMsg("A");

            Watch(persisting);
            Sys.Stop(persisting);
            ExpectTerminated(persisting);

            // now replay, but don't give the journal any tokens to replay events
            // so that we cause the timeout to trigger
            var replaying = Sys.ActorOf(Props.Create(() => new ReceiveTimeoutActor(timeout, probe.Ref)));
            Watch(replaying);

            // initial read highest
            SteppingMemoryJournal.Step(_journal);

            // read journal
            SteppingMemoryJournal.Step(_journal);

            // we should get initial receive timeout back from actor when replay completes
            probe.ExpectMsg(timeout);
        }
    }
}
