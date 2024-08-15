﻿// -----------------------------------------------------------------------
//  <copyright file="TimerPersistentActorSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Xunit;

namespace Akka.Persistence.Tests;

public class TimerPersistentActorSpec : PersistenceSpec
{
    public TimerPersistentActorSpec() : base(ConfigurationFactory.ParseString(@"
            akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""

            # snapshot store plugin is NOT defined, things should still work
            akka.persistence.snapshot-store.plugin = ""akka.persistence.no-snapshot-store""
            akka.persistence.snapshot-store.local.dir = ""target/snapshots-" + typeof(RecoveryPermitterSpec).FullName +
                                                                              "/"))
    {
    }

    [Fact]
    public void PersistentActor_with_Timer_must_not_discard_timer_msg_due_to_stashing()
    {
        var pa = ActorOf(TestPersistentActor.TestProps("p1"));
        pa.Tell("msg1");
        ExpectMsg("msg1");
    }

    [Fact]
    public void PersistentActor_with_Timer_must_handle_AutoReceivedMessages_automatically()
    {
        var pa = ActorOf(TestPersistentActor.TestProps("p3"));
        Watch(pa);
        pa.Tell(new AutoReceivedMessageWrapper(PoisonPill.Instance));
        ExpectTerminated(pa);
    }

    #region Actors

    internal class Scheduled
    {
        public Scheduled(object msg, IActorRef replyTo)
        {
            Msg = msg;
            ReplyTo = replyTo;
        }

        public object Msg { get; }
        public IActorRef ReplyTo { get; }
    }

    internal class AutoReceivedMessageWrapper
    {
        public AutoReceivedMessageWrapper(IAutoReceivedMessage msg)
        {
            Msg = msg;
        }

        public IAutoReceivedMessage Msg { get; }
    }

    internal class TestPersistentActor : PersistentActor, IWithTimers
    {
        private readonly string name;

        public TestPersistentActor(string name)
        {
            this.name = name;
        }

        public override string PersistenceId => name;

        public ITimerScheduler Timers { get; set; }

        public static Props TestProps(string name)
        {
            return Props.Create(() => new TestPersistentActor(name));
        }

        protected override bool ReceiveRecover(object message)
        {
            return true;
        }

        protected override bool ReceiveCommand(object message)
        {
            switch (message)
            {
                case Scheduled m:
                    m.ReplyTo.Tell(m.Msg);
                    return true;

                case AutoReceivedMessageWrapper m:
                    Timers.StartSingleTimer("PoisonPill", PoisonPill.Instance, TimeSpan.Zero);
                    return true;
                default:
                    Timers.StartSingleTimer("key", new Scheduled(message, Sender), TimeSpan.Zero);
                    Persist(message, _ => { });
                    return true;
            }
        }
    }

    #endregion
}