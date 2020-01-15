//-----------------------------------------------------------------------
// <copyright file="EndToEndEventAdapterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class MemoryEndToEndAdapterSpec : EndToEndEventAdapterSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"akka.persistence.journal.inmem.class = ""Akka.Persistence.Journal.SharedMemoryJournal, Akka.Persistence""");
        public MemoryEndToEndAdapterSpec() : base("inmem", Configuration("MemoryEndToEndAdapterSpec").WithFallback(Config))
        {
        }
    }

    internal class InternalTestKit : TestKitBase
    {
        public InternalTestKit(string actorSystemName, Config config) : base(new XunitAssertions(), config, actorSystemName)
        {
        }
    }

    public abstract class EndToEndEventAdapterSpec : PersistenceSpec
    {
        private readonly string _journalName;
        private readonly Config _journalConfig;

        #region Internal test classes

        internal interface IAppModel
        {
            object Payload { get; }
        }

        [Serializable]
        public sealed class A : IAppModel
        {
            public A(object payload)
            {
                Payload = payload;
            }

            public object Payload { get; private set; }
        }

        [Serializable]
        public sealed class B : IAppModel
        {
            public B(object payload)
            {
                Payload = payload;
            }

            public object Payload { get; private set; }
        }

        [Serializable]
        public sealed class NewA : IAppModel
        {
            public NewA(object payload)
            {
                Payload = payload;
            }

            public object Payload { get; private set; }
        }

        [Serializable]
        public sealed class NewB : IAppModel
        {
            public NewB(object payload)
            {
                Payload = payload;
            }

            public object Payload { get; private set; }
        }

        [Serializable]
        public sealed class Json
        {
            public readonly object Payload;

            public Json(object payload)
            {
                Payload = payload;
            }
        }

        public class AEndToEndAdapter : IEventAdapter
        {
            public AEndToEndAdapter(ExtendedActorSystem system)
            {
            }

            public string Manifest(object evt)
            {
                return evt.GetType().Name;
            }

            public object ToJournal(object evt)
            {
                if (evt is IAppModel) return new Json((evt as IAppModel).Payload);
                return null;
            }

            public IEventSequence FromJournal(object evt, string manifest)
            {
                Json m;
                if ((m = evt as Json) != null && m.Payload.ToString().StartsWith("a"))
                    return EventSequence.Single(new A(m.Payload));
                else
                    return EventSequence.Empty;
            }
        }

        public class NewAEndToEndAdapter : IEventAdapter
        {
            public NewAEndToEndAdapter(ExtendedActorSystem system)
            {
            }

            public string Manifest(object evt)
            {
                return evt.GetType().Name;
            }

            public object ToJournal(object evt)
            {
                if (evt is IAppModel) return new Json((evt as IAppModel).Payload);
                return null;
            }

            public IEventSequence FromJournal(object evt, string manifest)
            {
                Json m;
                if ((m = evt as Json) != null && m.Payload.ToString().StartsWith("a"))
                    return EventSequence.Single(new NewA(m.Payload));
                else
                    return EventSequence.Empty;
            }
        }

        public class BEndToEndAdapter : IEventAdapter
        {
            public BEndToEndAdapter(ExtendedActorSystem system)
            {
            }

            public string Manifest(object evt)
            {
                return evt.GetType().Name;
            }

            public object ToJournal(object evt)
            {
                if (evt is IAppModel) return new Json((evt as IAppModel).Payload);
                return null;
            }

            public IEventSequence FromJournal(object evt, string manifest)
            {
                Json m;
                if ((m = evt as Json) != null && m.Payload.ToString().StartsWith("b"))
                    return EventSequence.Single(new B(m.Payload));
                else
                    return EventSequence.Empty;
            }
        }

        public class NewBEndToEndAdapter : IEventAdapter
        {
            public NewBEndToEndAdapter(ExtendedActorSystem system)
            {
            }

            public string Manifest(object evt)
            {
                return evt.GetType().Name;
            }

            public object ToJournal(object evt)
            {
                if (evt is IAppModel) return new Json((evt as IAppModel).Payload);
                return null;
            }

            public IEventSequence FromJournal(object evt, string manifest)
            {
                Json m;
                if ((m = evt as Json) != null && m.Payload.ToString().StartsWith("b"))
                    return EventSequence.Single(new NewB(m.Payload));
                else
                    return EventSequence.Empty;
            }
        }

        public class EndToEndAdapterActor : NamedPersistentActor
        {
            private readonly LinkedList<object> _state = new LinkedList<object>();

            private readonly IActorRef _probe;
            public EndToEndAdapterActor(string name, string journalPluginId, IActorRef probe) : base(name)
            {
                _probe = probe;
                JournalPluginId = journalPluginId;
            }

            protected bool PersistIncoming(object message)
            {
                if (message is GetState)
                {
                    foreach (var e in _state)
                    {
                        Sender.Tell(e);
                    }
                }
                else
                {
                    var sender = Sender;
                    Persist(message, e =>
                    {
                        _state.AddLast(e);
                        sender.Tell(e);
                    });
                }
                return true;
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is RecoveryCompleted) ;
                else _state.AddLast(message);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                return PersistIncoming(message);
            }
        }

        #endregion

        protected readonly Config NoAdaptersConfig = ConfigurationFactory.Empty;
        protected readonly Config AdaptersConfig;
        private static readonly string AdaptersConfigFormat = @"
            akka.persistence.journal {{
                {0} {{
                    event-adapters {{
                        a = ""{1}+AEndToEndAdapter, Akka.Persistence.Tests""
                        b = ""{1}+BEndToEndAdapter, Akka.Persistence.Tests""
                    }}
                    event-adapter-bindings {{
                        # to journal
                        ""{1}+A, Akka.Persistence.Tests"" = a
                        ""{1}+B, Akka.Persistence.Tests"" = b

                        # from journal
                        ""{1}+Json, Akka.Persistence.Tests"" = [a,b]
                    }}
                }}
            }}";
        protected readonly Config NewAdaptersConfig;
        private static readonly string NewAdaptersConfigFormat = @"
            akka.persistence.journal {{
                {0} {{
                    event-adapters {{
                        a = ""{1}+NewAEndToEndAdapter, Akka.Persistence.Tests""
                        b = ""{1}+NewBEndToEndAdapter, Akka.Persistence.Tests""
                    }}
                    event-adapter-bindings {{
                        # to journal
                        ""{1}+A, Akka.Persistence.Tests"" = a
                        ""{1}+B, Akka.Persistence.Tests"" = b

                        # from journal
                        ""{1}+Json, Akka.Persistence.Tests"" = [a,b]
                    }}
                }}
            }}";

        protected EndToEndEventAdapterSpec(string journalName, Config journalConfig)
            : base(Configuration("EndToEndEventAdapterSpec"))
        {
            _journalName = journalName;
            _journalConfig = journalConfig;

            AdaptersConfig = string.Format(AdaptersConfigFormat, journalName, typeof(EndToEndEventAdapterSpec).FullName);
            NewAdaptersConfig = string.Format(NewAdaptersConfigFormat, journalName, typeof(EndToEndEventAdapterSpec).FullName);
        }

        private IActorRef Persister(string name, IActorRef probe, ActorSystem system)
        {
            return (system ?? Sys).ActorOf(Props.Create(() => new EndToEndAdapterActor(name, "akka.persistence.journal." + _journalName, probe)));
        }

        private T WithActorSystem<T>(string name, Config config, Func<ActorSystem, T> block)
        {
            var testKit = new InternalTestKit(name, _journalConfig.WithFallback(config));
            using (var system = testKit.Sys)
            {
                return block(system);
            }
        }

        [Fact]
        public void EventAdapters_in_end_to_end_scenarios_should_use_the_same_adapter_when_reading_as_was_used_when_writing_to_journal()
        {
            WithActorSystem("SimpleSystem", AdaptersConfig, system =>
            {
                var probe = new TestProbe(system, Assertions);

                var p1 = Persister("p1", probe, system);
                var a = new A("a1");
                var b = new B("b1");
                p1.Tell(a, probe.Ref);
                p1.Tell(b, probe.Ref);

                probe.ExpectMsg<A>(x => x.Payload.Equals("a1"));
                probe.ExpectMsg<B>(x => x.Payload.Equals("b1"));

                probe.Watch(p1);
                p1.Tell(PoisonPill.Instance);
                probe.ExpectTerminated(p1);

                var p11 = Persister("p1", probe, system);
                p11.Tell(GetState.Instance, probe.Ref);

                probe.ExpectMsg<A>(x => x.Payload.Equals("a1"));
                probe.ExpectMsg<B>(x => x.Payload.Equals("b1"));

                return true;
            });
        }

        [Fact]
        public void EventAdapters_in_end_to_end_scenarios_should_allow_using_an_adapter_when_write_was_performed_without_an_adapter()
        {
            var pid = "p2";
            WithActorSystem("NoAdapterSystem", AdaptersConfig, system =>
            {
                var probe = new TestProbe(system, Assertions);

                var p2 = Persister(pid, probe, system);
                var a = new A("a1");
                var b = new B("b1");
                p2.Tell(a, probe.Ref);
                p2.Tell(b, probe.Ref);

                probe.ExpectMsg<A>(x => x.Payload.Equals("a1"));
                probe.ExpectMsg<B>(x => x.Payload.Equals("b1"));

                probe.Watch(p2);
                p2.Tell(PoisonPill.Instance);
                probe.ExpectTerminated(p2);

                var p21 = Persister(pid, probe, system);
                p21.Tell(GetState.Instance, probe.Ref);

                probe.ExpectMsg<A>(x => x.Payload.Equals("a1"));
                probe.ExpectMsg<B>(x => x.Payload.Equals("b1"));

                return true;
            });

            WithActorSystem("NoAdaptersAdded", NewAdaptersConfig, system =>
            {
                var probe = new TestProbe(system, Assertions);
                var p22 = Persister(pid, probe, system);
                p22.Tell(GetState.Instance, probe.Ref);

                probe.ExpectMsg<NewA>(x => x.Payload.Equals("a1"));
                probe.ExpectMsg<NewB>(x => x.Payload.Equals("b1"));

                return true;
            });
        }

        [Fact]
        public void EventAdapters_in_end_to_end_scenarios_should_give_nice_error_message_when_unable_to_play_back_as_adapter_does_not_exist()
        {
            // after some time we start the system a-new
            // and the adapter originally used for adapting A is missing from the configuration!
            var journalPath = string.Format("akka.persistence.journal.{0}", _journalName);

            // TODO when WithoutPath is added
            // var missingAdapterConfig = AdaptersConfig.WithoutPath(...)
        }
    }
}
