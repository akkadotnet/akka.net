//-----------------------------------------------------------------------
// <copyright file="xVersionJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Journal
{
    public abstract class xVersionJournalSpec : PluginSpec
    {
        protected static readonly Config Config =
            ConfigurationFactory.ParseString("akka.persistence.publish-plugin-commands = on");

        private static readonly string _specConfigTemplate = @"
            akka.persistence {{
                publish-plugin-commands = on
                journal {{
                    plugin = ""akka.persistence.journal.testspec""
                    testspec {{
                        class = ""{0}""
                        plugin-dispatcher = ""akka.actor.default-dispatcher""
                    }}
                }}
            }}
        ";

        private TestProbe _senderProbe;
        private TestProbe _receiverProbe;

        protected xVersionJournalSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null)
            : base(FromConfig(config).WithFallback(Config), actorSystemName ?? "xVersionJournalSpec", output)
        {
        }

        /// <summary>
        /// Initializes a journal with set o predefined messages.
        /// </summary>
        protected IEnumerable<AtomicWrite> Initialize()
        {
            _senderProbe = CreateTestProbe();
            _receiverProbe = CreateTestProbe();
            PreparePersistenceId(Pid);
            return WriteMessages(1, 5, Pid, _senderProbe.Ref, WriterGuid);
        }

        protected xVersionJournalSpec(Type journalType, string actorSystemName = null, ITestOutputHelper output = null)
            : base(ConfigFromTemplate(journalType), actorSystemName, output)
        {
        }

        /// <summary>
        /// Overridable hook that is called before populating the journal for the next test case.
        /// <paramref name="pid"/> is the persistenceId that will be used in the test.
        /// This method may be needed to clean pre-existing events from the log.
        /// </summary>
        /// <param name="pid"></param>
        protected virtual void PreparePersistenceId(string pid)
        {
        }

        /// <summary>
        /// Implementation may override and return false if it does not support
        /// atomic writes of several events, as emitted by
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent},Action{TEvent})"/>.
        /// </summary>
        protected virtual bool SupportsAtomicPersistAllOfSeveralEvents { get { return true; } }

        /// <summary>
        /// When true enables tests which check if the Journal properly rejects
        /// writes of objects which are not serializable.
        /// </summary>
        protected virtual bool SupportsRejectingNonSerializableObjects { get { return true; } }

        protected IActorRef Journal { get { return Extension.JournalFor(null); } }

        private static Config ConfigFromTemplate(Type journalType)
        {
            var config = string.Format(_specConfigTemplate, journalType.AssemblyQualifiedName);
            return ConfigurationFactory.ParseString(config);
        }

        protected bool IsReplayedMessage(ReplayedMessage message, long seqNr, bool isDeleted = false)
        {
            var p = message.Persistent;
            return p.IsDeleted == isDeleted
                   && p.Payload.ToString() == "a-" + seqNr
                   && p.PersistenceId == Pid
                   && p.SequenceNr == seqNr;
        }

        private AtomicWrite[] WriteMessages(int from, int to, string pid, IActorRef sender, string writerGuid)
        {
            return new AtomicWrite[0];
        }

        [Fact]
        public void Journal_should_replay_all_messages()
        {
            Journal.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, Pid, _receiverProbe.Ref));
            for (int i = 1; i <= 5; i++) _receiverProbe.ExpectMsg<ReplayedMessage>(m => IsReplayedMessage(m, i));
            _receiverProbe.ExpectMsg<RecoverySuccess>();
        }
    }
}