//-----------------------------------------------------------------------
// <copyright file="MemoryEventAdapterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class MemoryEventAdapterSpec : PersistenceSpec
    {
        #region Internal test classes

        public interface IJournalModel
        {
            object Payload { get; }
            ISet<string> Tags { get; }
        }

        [Serializable]
        public sealed class Tagged : IJournalModel, IEquatable<IJournalModel>
        {
            public object Payload { get; private set; }
            public ISet<string> Tags { get; private set; }

            public Tagged(object payload, ISet<string> tags)
            {
                Payload = payload;
                Tags = tags;
            }

            public bool Equals(IJournalModel other)
            {
                return other != null && Payload.Equals(other.Payload) && Tags.SetEquals(other.Tags);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as IJournalModel);
            }
        }

        [Serializable]
        public sealed class NotTagged : IJournalModel, IEquatable<IJournalModel>
        {
            public object Payload { get; private set; }
            public ISet<string> Tags { get { return new HashSet<string>(); } }

            public NotTagged(object payload)
            {
                Payload = payload;
            }

            public bool Equals(IJournalModel other)
            {
                return other != null && Payload.Equals(other.Payload) && Tags.SetEquals(other.Tags);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as IJournalModel);
            }
        }

        public interface IDomainEvent { }

        [Serializable]
        public sealed class TaggedDataChanged : IDomainEvent, IEquatable<TaggedDataChanged>
        {
            public readonly ISet<string> Tags;
            public readonly int Value;

            public TaggedDataChanged(ISet<string> tags, int value)
            {
                Tags = tags;
                Value = value;
            }

            public bool Equals(TaggedDataChanged other)
            {
                return other != null && Tags.SetEquals(other.Tags) && Value == other.Value;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as TaggedDataChanged);
            }
        }

        [Serializable]
        public sealed class UserDataChanged : IDomainEvent, IEquatable<UserDataChanged>
        {
            public readonly string CountryCode;
            public readonly int Age;

            public UserDataChanged(string countryCode, int age)
            {
                CountryCode = countryCode;
                Age = age;
            }

            public bool Equals(UserDataChanged other)
            {
                return other != null && Age == other.Age && CountryCode.Equals(other.CountryCode);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as UserDataChanged);
            }
        }

        public class UserAgeTaggingAdapter : IEventAdapter
        {
            public readonly ISet<string> Adult = new HashSet<string> { "adult" };
            public readonly ISet<string> Minor = new HashSet<string> { "minor" };

            public string Manifest(object evt)
            {
                return string.Empty;
            }

            public object ToJournal(object evt)
            {
                var e = evt as UserDataChanged;
                if (e == null) return new NotTagged(evt);
                return new Tagged(e, e.Age > 18 ? Adult : Minor);
            }

            public virtual IEventSequence FromJournal(object evt, string manifest)
            {
                IJournalModel m;
                return EventSequence.Single((m = evt as IJournalModel) != null ? m.Payload : null);
            }
        }

        public class ReplayPassThroughAdapter : UserAgeTaggingAdapter
        {
            public override IEventSequence FromJournal(object evt, string manifest)
            {
                // don't unpack, just pass through the JournalModel
                return EventSequence.Single(evt);
            }
        }

        public class LoggingAdapter : IEventAdapter
        {
            public readonly ILoggingAdapter Log;
            public LoggingAdapter(ExtendedActorSystem system)
            {
                Log = system.Log;
            }

            public string Manifest(object evt)
            {
                return string.Empty;
            }

            public object ToJournal(object evt)
            {
                Log.Info("On it's way to the journal: {0}", evt);
                return evt;
            }

            public IEventSequence FromJournal(object evt, string manifest)
            {
                Log.Info("On it's way from the journal: {0}", evt);
                return EventSequence.Single(evt);
            }
        }

        public class PersistAllIncomingActor : NamedPersistentActor
        {
            public readonly LinkedList<object> State = new LinkedList<object>();

            public PersistAllIncomingActor(string name, string journalPluginId) : base(name)
            {
                JournalPluginId = journalPluginId;
            }

            private bool PersistIncoming(object message)
            {
                if (message is GetState)
                    foreach (var e in State)
                    {
                        Sender.Tell(e);
                    }
                else
                {
                    var sender = Sender;
                    Persist(message, e =>
                    {
                        State.AddLast(e);
                        sender.Tell(e);
                    });
                }
                return true;
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is RecoveryCompleted) ;
                else
                {
                    State.AddLast(message);
                }
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                return PersistIncoming(message);
            }
        }

        #endregion

        private readonly string _journalName;
        private static readonly string JournalModelTypeName = typeof(IJournalModel).FullName + ", Akka.Persistence.Tests";
        private static readonly string DomainEventTypeName = typeof(IDomainEvent).FullName + ", Akka.Persistence.Tests";

        private static readonly string _configFormat = @"
            akka.persistence.journal {{
                common-event-adapters {{
                    age = ""{0}""
                    replay-pass-through = ""{1}""
                }}
                inmem {{
                    # change to path reference $akka.persistence.journal.common-event-adapters
                    event-adapters {{
                        age = ""{0}""
                        replay-pass-through = ""{1}""
                    }}
                    event-adapter-bindings {{
                        ""{2}"" = age
                        ""{3}"" = age
                    }}
                }}
                with-actor-system {{
                    class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                    dispatcher = default-dispatcher
                    dir = ""journal-1""

                    event-adapters {{
                        logging = ""{4}""
                    }}
                    event-adapters-bindings {{
                        ""System.Object"" = logging
                    }}
                }}
                replay-pass-through-adapter-journal {{
                    class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                    dispatcher = default-dispatcher
                    dir = ""journal-2""

                    # change to path reference $akka.persistence.journal.common-event-adapters
                    event-adapters {{
                        age = ""{0}""
                        replay-pass-through = ""{1}""
                    }}
                    event-adapter-bindings {{
                        ""{2}"" = replay-pass-through
                        ""{3}"" = replay-pass-through
                    }}
                }}
                no-adapter {{
                    class = ""Akka.Persistence.Journal.MemoryJournal, Akka.Persistence""
                    dispatcher = default-dispatcher
                    dir = ""journal-3""                    
                }}
            }}";

        public static readonly string AdapterSpecConfig = string.Format(_configFormat,
            typeof(UserAgeTaggingAdapter).FullName + ", Akka.Persistence.Tests",
            typeof(ReplayPassThroughAdapter).FullName + ", Akka.Persistence.Tests",
            DomainEventTypeName,
            JournalModelTypeName,
            typeof(LoggingAdapter).FullName + ", Akka.Persistence.Tests");

        public MemoryEventAdapterSpec()
            : this("inmem", Configuration("MemoryEventAdapterSpec"), ConfigurationFactory.ParseString(AdapterSpecConfig))
        {

        }

        protected MemoryEventAdapterSpec(string journalName, Config journalConfig, Config adapterConfig)
            : base(journalConfig.WithFallback(adapterConfig))
        {
            _journalName = journalName;
        }

        private IActorRef Persister(string name, string journalName = null)
        {
            return Sys.ActorOf(Props.Create(() => new PersistAllIncomingActor(name, "akka.persistence.journal." + (journalName ?? _journalName))));
        }

        private object ToJournal(object message, string journalName = null)
        {
            journalName = string.IsNullOrEmpty(journalName) ? _journalName : journalName;
            return Persistence.Instance.Apply(Sys).AdaptersFor("akka.persistence.journal." + journalName).Get(message.GetType()).ToJournal(message);
        }

        private object FromJournal(object message, string journalName = null)
        {
            journalName = string.IsNullOrEmpty(journalName) ? _journalName : journalName;
            return Persistence.Instance.Apply(Sys).AdaptersFor("akka.persistence.journal." + journalName).Get(message.GetType()).FromJournal(message, string.Empty);
        }

        [Fact]
        public void EventAdapter_should_wrap_with_tags()
        {
            var e = new UserDataChanged("name", 42);
            ToJournal(e).ShouldBe(new Tagged(e, new HashSet<string> { "adult" }));
        }

        [Fact]
        public void EventAdapter_should_unwrap_when_reading()
        {
            var e = new UserDataChanged("name", 42);
            var tagged = new Tagged(e, new HashSet<string> { "adult" });

            ToJournal(e).ShouldBe(tagged);
            FromJournal(tagged, string.Empty).ShouldBe(new SingleEventSequence(e));
        }

        [Fact]
        public void EventAdapter_should_create_adapter_requiring_actor_system()
        {
            var e = new UserDataChanged("name", 42);

            ToJournal(e, "with-actor-system").ShouldBe(e);
            FromJournal(e, "with-actor-system").ShouldBe(new SingleEventSequence(e));
        }

        [Fact]
        public void EventAdapter_should_store_events_after_applying_adapter()
        {
            var replayPassThroughJournalId = "replay-pass-through-adapter-journal";

            var p1 = Persister("p1", replayPassThroughJournalId);
            var m1 = new UserDataChanged("name", 64);
            var m2 = "hello";

            p1.Tell(m1);
            p1.Tell(m2);
            ExpectMsg(m1);
            ExpectMsg(m2);

            Watch(p1);
            p1.Tell(PoisonPill.Instance);
            ExpectTerminated(p1);

            var p11 = Persister("p1", replayPassThroughJournalId);
            p11.Tell(GetState.Instance);
            ExpectMsg(new Tagged(m1, new HashSet<string> { "adult" }));
            ExpectMsg(m2);
        }

        [Fact]
        public void EventAdapter_should_work_when_plugin_defines_no_adapter()
        {
            var noAdapter = "no-adapter";

            var p1 = Persister("p1", noAdapter);
            var m1 = new UserDataChanged("name", 64);
            var m2 = "hello";

            p1.Tell(m1);
            p1.Tell(m2);
            ExpectMsg(m1);
            ExpectMsg(m2);

            Watch(p1);
            p1.Tell(PoisonPill.Instance);
            ExpectTerminated(p1);

            var p11 = Persister("p1", noAdapter);
            p11.Tell(GetState.Instance);
            ExpectMsg(m1);
            ExpectMsg(m2);
        }
    }
}
