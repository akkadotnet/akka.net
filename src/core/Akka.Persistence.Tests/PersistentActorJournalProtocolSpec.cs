//-----------------------------------------------------------------------
// <copyright file="PersistentActorJournalProtocolSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class PersistentActorJournalProtocolSpec : AkkaSpec

    {
        public interface ICommand
        {
        }

        public class Persist : ICommand
        {
            public int Id { get; private set; }
            public IList<object> Messages { get; private set; }

            public Persist(int id, params object[] messages)
            {
                Id = id;
                Messages = messages;
            }

            protected bool Equals(Persist other)
            {
                return Id == other.Id && Messages.SequenceEqual(other.Messages);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((Persist) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Id*397) ^ (Messages != null ? Messages.GetHashCode() : 0);
                }
            }
        }

        public class PersistAsync : ICommand
        {
            public int Id { get; private set; }
            public IList<object> Messages { get; private set; }

            public PersistAsync(int id, params object[] messages)
            {
                Id = id;
                Messages = messages;
            }

            protected bool Equals(PersistAsync other)
            {
                return Id == other.Id && Messages.SequenceEqual(other.Messages);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((PersistAsync) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Id*397) ^ (Messages != null ? Messages.GetHashCode() : 0);
                }
            }
        }

        public class Multi : ICommand
        {
            public IList<ICommand> Commands { get; private set; }

            public Multi(params ICommand[] commands)
            {
                Commands = commands;
            }
        }

        public class Echo : ICommand
        {
            public int Id { get; private set; }

            public Echo(int id)
            {
                Id = id;
            }
        }

        public class Fail : ICommand
        {
            public Exception Exception { get; private set; }

            public Fail(Exception exception)
            {
                Exception = exception;
            }
        }

        public class Done : ICommand
        {
            public int Id { get; private set; }
            public int Sub { get; private set; }

            public Done(int id, int sub)
            {
                Id = id;
                Sub = sub;
            }

            public override string ToString()
            {
                return $"Id: {Id}, Sub: {Sub}";
            }
        }

        public class PreStart : ICommand
        {
            public string Name { get; private set; }

            public PreStart(string name)
            {
                Name = name;
            }
        }

        public class PreRestart : ICommand
        {
            public string Name { get; private set; }

            public PreRestart(string name)
            {
                Name = name;
            }
        }

        public class PostRestart : ICommand
        {
            public string Name { get; private set; }

            public PostRestart(string name)
            {
                Name = name;
            }
        }

        public class PostStop : ICommand
        {
            public string Name { get; private set; }

            public PostStop(string name)
            {
                Name = name;
            }
        }

        public class A : PersistentActor
        {
            private readonly IActorRef _monitor;

            public A(IActorRef monitor)
            {
                _monitor = monitor;
            }

            public override string PersistenceId
            {
                get { return Self.Path.Name; }
            }

            protected override void PreStart()
            {
                _monitor.Tell(new PreStart(PersistenceId));
            }

            protected override void PreRestart(Exception reason, object message)
            {
                _monitor.Tell(new PreRestart(PersistenceId));
            }

            protected override void PostRestart(Exception reason)
            {
                _monitor.Tell(new PostRestart(PersistenceId));
            }

            protected override void PostStop()
            {
                _monitor.Tell(new PostStop(PersistenceId));
            }

            protected override bool ReceiveRecover(object message)
            {
                _monitor.Tell(message);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!Behavior(message))
                {
                    if (message is Multi)
                    {
                        foreach (var command in ((Multi) message).Commands)
                            Behavior(command);
                        return true;
                    }
                    return false;
                }
                return true;
            }

            private bool Behavior(object message)
            {
                if (message is Persist)
                    P((Persist) message);
                else if (message is PersistAsync)
                    PA((PersistAsync) message);
                else if (message is Echo)
                    Sender.Tell(new Done(((Echo) message).Id, 0));
                else if (message is Fail)
                    throw ((Fail) message).Exception;
                else return false;
                return true;
            }

            private void DoNothing()
            {
            }

            private void P(Persist p)
            {
                var sub = 0;
                PersistAll(p.Messages, e =>
                {
                    Sender.Tell(new Done(p.Id, ++sub));
                    if (!Behavior(e))
                        DoNothing();
                });
            }

            private void PA(PersistAsync p)
            {
                var sub = 0;
                PersistAllAsync(p.Messages, e =>
                {
                    Sender.Tell(new Done(p.Id, ++sub));
                    if (!Behavior(e))
                        DoNothing();
                });
            }
        }

        public class JournalProbeExtension : ExtensionIdProvider<JournalProbe>
        {
            public static readonly JournalProbeExtension Instance = new JournalProbeExtension();

            private JournalProbeExtension()
            {
            }

            public override JournalProbe CreateExtension(ExtendedActorSystem system)
            {
                return new JournalProbe(system);
            }
        }

        public class JournalProbe : IExtension
        {
            public TestProbe Probe { get; private set; }
            public IActorRef Ref { get; private set; }

            public JournalProbe(ExtendedActorSystem system)
            {
                Probe = new TestProbe(system, new XunitAssertions());
                Ref = Probe.Ref;
            }
        }

        public class JournalPuppet : ActorBase
        {
            private readonly IActorRef _ref;

            public JournalPuppet()
            {
                _ref = JournalProbeExtension.Instance.Apply(Context.System).Ref;
            }

            protected override bool Receive(object message)
            {
                _ref.Forward(message);
                return true;
            }
        }

        private static readonly Config Config = ConfigurationFactory.ParseString(@"
puppet {
  class = ""Akka.Persistence.Tests.PersistentActorJournalProtocolSpec+JournalPuppet, Akka.Persistence.Tests""
  max-message-batch-size = 10
}
akka.persistence.journal.plugin = puppet
akka.persistence.snapshot-store.plugin = ""akka.persistence.no-snapshot-store""");

        private readonly TestProbe _journal;

        public PersistentActorJournalProtocolSpec() : base(Config)
        {
            _journal = JournalProbeExtension.Instance.Apply(Sys).Probe;
        }

        public class Msgs
        {
            public IList<object> Messages { get; private set; }

            public Msgs(params object[] messages)
            {
                Messages = messages;
            }
        }

        private WriteMessages ExpectWrite(IActorRef subject, params Msgs[] msgs)
        {
            var w = _journal.ExpectMsg<WriteMessages>();
            w.PersistentActor.ShouldBe(subject);
            var messages = w.Messages.ToList();
            messages.Count.ShouldBe(msgs.Length);
            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                var msg = msgs[i];
                if (message is AtomicWrite)
                {
                    var aw = ((AtomicWrite) message);
                    var writes = ((IEnumerable<IPersistentRepresentation>) aw.Payload).ToList();
                    writes.Count.ShouldBe(msg.Messages.Count);
                    for (int j = 0; j < writes.Count; j++)
                    {
                        var p = writes[j];
                        var evt = p.Payload;
                        evt.ShouldBe(msg.Messages[j]);
                    }
                }
                else Assertions.Fail("unexpected ", message);
            }
            return w;
        }

        private void Confirm(WriteMessages w)
        {
            _journal.Send(w.PersistentActor, WriteMessagesSuccessful.Instance);
            foreach (var message in w.Messages)
            {
                if (message is AtomicWrite)
                {
                    var msgs = (IEnumerable<IPersistentRepresentation>) ((AtomicWrite) message).Payload;
                    foreach (var msg in msgs)
                    {
                        w.PersistentActor.Tell(new WriteMessageSuccess(msg, w.ActorInstanceId), msg.Sender);
                    }
                }
                else
                    w.PersistentActor.Tell(message.Payload, message.Sender);
            }
        }

        private IActorRef StartActor(string name)
        {
            var subject = Sys.ActorOf(Props.Create(() => new A(TestActor)), name);
            subject.Tell(new Echo(0));
            ExpectMsg<PreStart>(m => m.Name.Equals(name));
            _journal.ExpectMsg<ReplayMessages>();
            _journal.Reply(new RecoverySuccess(0L));
            ExpectMsg<RecoveryCompleted>();
            ExpectMsg<Done>(m => m.Id == 0 && m.Sub == 0);
            return subject;
        }

        [Fact]
        public void PersistentActor_journal_protocol_should_not_send_WriteMessages_while_a_write_is_still_outstanding_when_using_simple_Persist()
        {
            var subject = StartActor("test-1");
            subject.Tell(new Persist(1, "a-1"));
            var w1 = ExpectWrite(subject, new Msgs("a-1"));
            subject.Tell(new Persist(2, "a-2"));
            ExpectNoMsg(TimeSpan.FromMilliseconds(300));
            _journal.HasMessages.ShouldBeFalse();
            Confirm(w1);
            ExpectMsg<Done>(m => m.Id == 1 && m.Sub == 1);
            var w2 = ExpectWrite(subject, new Msgs("a-2"));
            Confirm(w2);
            ExpectMsg<Done>(m => m.Id == 2 && m.Sub == 1);
            subject.Tell(PoisonPill.Instance);
            ExpectMsg<PostStop>(m=> m.Name.Equals("test-1"));
            _journal.HasMessages.ShouldBeFalse();
        }

        [Fact]
        public void PersistentActor_journal_protocol_should_not_send_WriteMessages_while_a_write_is_still_outstanding_when_using_nested_Persist()
        {
            var subject = StartActor("test-2");
            subject.Tell(new Persist(1, new Persist(2, "a-1")));
            var w1 = ExpectWrite(subject, new Msgs(new Persist(2, "a-1")));
            subject.Tell(new Persist(3, "a-2"));
            ExpectNoMsg(TimeSpan.FromMilliseconds(300));
            _journal.HasMessages.ShouldBeFalse();
            Confirm(w1);
            ExpectMsg<Done>(m => m.Id == 1 && m.Sub == 1);
            var w2 = ExpectWrite(subject, new Msgs("a-1"));
            Confirm(w2);
            ExpectMsg<Done>(m => m.Id == 2 && m.Sub == 1);
            var w3 = ExpectWrite(subject, new Msgs("a-2"));
            Confirm(w3);
            ExpectMsg<Done>(m => m.Id == 3 && m.Sub == 1);
            subject.Tell(PoisonPill.Instance);
            ExpectMsg<PostStop>(m=> m.Name.Equals("test-2"));
            _journal.HasMessages.ShouldBeFalse();
        }

        [Fact]
        public void PersistentActor_journal_protocol_should_not_send_WriteMessages_while_a_write_is_still_outstanding_when_using_nested_multiple_Persist()
        {
            var subject = StartActor("test-3");
            subject.Tell(new Multi(new Persist(1, new Persist(2, "a-1")), new Persist(3, "a-2")));
            var w1 = ExpectWrite(subject, new Msgs(new Persist(2, "a-1")), new Msgs("a-2"));
            Confirm(w1);
            ExpectMsg<Done>(m => m.Id == 1 && m.Sub == 1);
            ExpectMsg<Done>(m => m.Id == 3 && m.Sub == 1);
            var w2 = ExpectWrite(subject, new Msgs("a-1"));
            Confirm(w2);
            ExpectMsg<Done>(m => m.Id == 2 && m.Sub == 1);
            subject.Tell(PoisonPill.Instance);
            ExpectMsg<PostStop>(m=> m.Name.Equals("test-3"));
            _journal.HasMessages.ShouldBeFalse();
        }

        [Fact]
        public void PersistentActor_journal_protocol_should_not_send_WriteMessages_while_a_write_is_still_outstanding_when_using_large_number_of_Persist_calls()
        {
            var subject = StartActor("test-4");
            subject.Tell(new Multi(Enumerable.Range(0, 30).Select(i => new Persist(i, "a-" + i)).Cast<ICommand>().ToArray()));
            var w1 = ExpectWrite(subject, Enumerable.Range(0, 30).Select(i => new Msgs("a-" + i)).ToArray());
            Confirm(w1);
            for (int i = 0; i < 30; i++)
            {
                ExpectMsg<Done>(m => m.Id == i && m.Sub == 1);
            }
            subject.Tell(PoisonPill.Instance);
            ExpectMsg<PostStop>(m=> m.Name.Equals("test-4"));
            _journal.HasMessages.ShouldBeFalse();
        }

        [Fact]
        public void PersistentActor_journal_protocol_should_not_send_WriteMessages_while_a_write_is_still_outstanding_when_using_large_number_of_PersistAsync_calls()
        {
            Func<int, int, Msgs[]> msgs = (start, end) =>
                Enumerable.Range(start, end - start).Select(i => new Msgs("a-" + i + "-1", "a-" + i + "-2")).ToArray();
            Func<int, int, ICommand[]> commands = (start, end) =>
                Enumerable.Range(start, end - start)
                    .Select(i => new PersistAsync(i, "a-" + i + "-1", "a-" + i + "-2"))
                    .Cast<ICommand>()
                    .ToArray();
            Action<int, int> expectDone = (start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    for (int j = 1; j <= 2; j++)
                    {
                        ExpectMsg<Done>(m => m.Id == i && m.Sub == j);
                    }
                }
            };

            var subject = StartActor("test-5");
            subject.Tell(new PersistAsync(-1, new List<object> { "a" }.Union(commands(20, 30)).ToArray()));
            subject.Tell(new Multi(commands(0, 10)));
            subject.Tell(new Multi(commands(10, 20)));
            var w0 = ExpectWrite(subject, new Msgs(new List<object> { "a" }.Union(commands(20, 30)).ToArray()));
            _journal.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
            Confirm(w0);
            for (int i = 1; i <= 11; i++)
            {
                ExpectMsg<Done>(m => m.Id == -1 && m.Sub == i);
            }
            var w1 = ExpectWrite(subject, msgs(0, 20));
            _journal.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
            Confirm(w1);
            expectDone(0, 20);
            var w2 = ExpectWrite(subject, msgs(20, 30));
            Confirm(w2);
            expectDone(20, 30);
            subject.Tell(PoisonPill.Instance);
            ExpectMsg<PostStop>(m=> m.Name.Equals("test-5"));
            _journal.HasMessages.ShouldBeFalse();
        }

        [Fact]
        public void PersistentActor_journal_protocol_should_not_send_WriteMessages_while_a_write_is_still_outstanding_when_the_actor_fails_with_queued_events()
        {
            var subject = StartActor("test-6");
            subject.Tell(new PersistAsync(1, "a-1"));
            var w1 = ExpectWrite(subject, new Msgs("a-1"));
            subject.Tell(new PersistAsync(2, "a-2"));
            EventFilter.Exception<Exception>("K-BOOM!").ExpectOne(() =>
            {
                subject.Tell(new Fail(new Exception("K-BOOM!")));
                ExpectMsg<PreRestart>(m => m.Name.Equals("test-6"));
                ExpectMsg<PostRestart>(m => m.Name.Equals("test-6"));
                _journal.ExpectMsg<ReplayMessages>();
            });
            _journal.Reply(new RecoverySuccess(0L));
            ExpectMsg<RecoveryCompleted>();
            Confirm(w1);
            subject.Tell(PoisonPill.Instance);
            ExpectMsg<PostStop>(m=> m.Name.Equals("test-6"));
            _journal.HasMessages.ShouldBeFalse();
        }
    }
}
