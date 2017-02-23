//-----------------------------------------------------------------------
// <copyright file="JournalSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Fsm;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Serialization
{
    public abstract class JournalSerializationSpec : PluginSpec
    {
        protected JournalSerializationSpec(Config config, string actorSystem, ITestOutputHelper output) : base(config, actorSystem, output)
        {
        }

        protected IActorRef Journal => Extension.JournalFor(null);

        [Fact]
        public virtual void Journal_should_serialize_Persistent()
        {
            var probe = CreateTestProbe();
            var sequenceNr = 1L;
            var persistentEvent = new Persistent("string payload", sequenceNr, Pid, null, false, null, WriterGuid);

            var messages = new List<AtomicWrite>
            {
                new AtomicWrite(persistentEvent)
            };

            Journal.Tell(new WriteMessages(messages, probe.Ref, ActorInstanceId));
            probe.ExpectMsg<WriteMessagesSuccessful>();
            probe.ExpectMsg<WriteMessageSuccess>(m => m.ActorInstanceId == ActorInstanceId && m.Persistent.PersistenceId == Pid);

            Journal.Tell(new ReplayMessages(0, sequenceNr, long.MaxValue, Pid, probe.Ref));
            probe.ExpectMsg<ReplayedMessage>(s => s.Persistent.PersistenceId == persistentEvent.PersistenceId
                    && s.Persistent.SequenceNr == persistentEvent.SequenceNr
                    && s.Persistent.Payload.Equals(persistentEvent.Payload));
            probe.ExpectMsg<RecoverySuccess>();
        }

        [Fact]
        public virtual void Journal_should_serialize_StateChangeEvent()
        {
            var probe = CreateTestProbe();
            var stateChangeEvent = new PersistentFSM.StateChangeEvent("init", TimeSpan.FromSeconds(342));

            var messages = new List<AtomicWrite>
            {
                new AtomicWrite(new Persistent(stateChangeEvent, 1, Pid))
            };

            Journal.Tell(new WriteMessages(messages, probe.Ref, ActorInstanceId));
            probe.ExpectMsg<WriteMessagesSuccessful>();
            probe.ExpectMsg<WriteMessageSuccess>(m => m.ActorInstanceId == ActorInstanceId && m.Persistent.PersistenceId == Pid);

            Journal.Tell(new ReplayMessages(0, 1, long.MaxValue, Pid, probe.Ref));
            probe.ExpectMsg<ReplayedMessage>();
            probe.ExpectMsg<RecoverySuccess>();
        }
    }
}
