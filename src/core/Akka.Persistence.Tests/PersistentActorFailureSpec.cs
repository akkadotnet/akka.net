//-----------------------------------------------------------------------
// <copyright file="PersistentActorFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.TestKit.Internal;
using Akka.TestKit.TestEvent;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Persistence.Tests
{

    public class PersistentActorFailureSpec : PersistenceSpec
    {
        #region internal test classes

        internal class SimulatedException : Exception
        {
            public SimulatedException()
            {
            }

            public SimulatedException(string message) : base(message)
            {
            }
        }

        internal class SimulatedSerializationException : Exception
        {
            public SimulatedSerializationException()
            {
            }

            public SimulatedSerializationException(string message) : base(message)
            {
            }
        }

        internal class FailingMemoryJournal : MemoryJournal
        {

            protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
            {
                var msgs = messages.ToList();
                if (IsWrong(msgs))
                    throw new SimulatedException("Simulated Store failure");
                var checkSerializable = CheckSerializable(msgs);
                if (checkSerializable.Any(cs => cs != null))
                {
                    return Task.FromResult(checkSerializable);
                }
                return base.WriteMessagesAsync(msgs);
            }

            public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr,
                long toSequenceNr, long max,
                Action<IPersistentRepresentation> recoveryCallback)
            {
                var highest = HighestSequenceNr(persistenceId);
                var readFromStore =
                    Read(persistenceId, fromSequenceNr, toSequenceNr, max).ToArray();

                if (readFromStore.Length == 0) return Task.FromResult(new object());
                if (IsCorrupt(readFromStore))
                {
                    var promise = new TaskCompletionSource<object>();
                    promise.SetException( new SimulatedException(string.Format("blahonga {0} {1}", fromSequenceNr, toSequenceNr)));
                    return promise.Task;
                }
                readFromStore.ForEach(recoveryCallback);
                return Task.FromResult(new object());
            }

            private bool IsWrong(IEnumerable<AtomicWrite> messages)
            {
                return
                    messages.Any(
                        a =>
                            ((IEnumerable<IPersistentRepresentation>) a.Payload).Any(
                                p => ((string) ((Evt) p.Payload).Data).Contains("wrong")));
            }

            private IImmutableList<Exception> CheckSerializable(IEnumerable<AtomicWrite> messages)
            {
                return
                    messages
                        .Select(a =>
                        {
                            var notSerializable = (((IEnumerable<IPersistentRepresentation>) a.Payload).FirstOrDefault(
                                p => ((string) ((Evt) p.Payload).Data).Contains("not serializable")));
                            if (notSerializable != null)
                            {
                                return new SimulatedSerializationException((string) ((Evt) notSerializable.Payload).Data);
                            }
                            return (Exception) null;

                        }).ToImmutableList();
            }

            private bool IsCorrupt(IPersistentRepresentation[] events)
            {
                return events.Any(p => ((string) ((Evt) p.Payload).Data).Contains("corrupt"));
            }
        }

        internal class OnRecoveryFailurePersistentActor : PersistentActorSpec.ExamplePersistentActor
        {
            private readonly IActorRef _probe;

            public OnRecoveryFailurePersistentActor(string name, IActorRef probe) : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Persist(new Evt(cmd.Data), evt => UpdateState(evt));

                        return true;
                    }
                }
                else return true;
                return false;
            }

            protected override void OnRecoveryFailure(Exception reason, object message = null)
            {
                _probe.Tell("recovery-failure:" + reason.Message);
            }
        }

        internal class Supervisor : ActorBase
        {
            protected readonly IActorRef TestActor;

            public Supervisor(IActorRef testActor)
            {
                TestActor = testActor;
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(exception =>
                {
                    TestActor.Tell(exception);
                    return Directive.Restart;
                });
            }

            protected override bool Receive(object message)
            {
                var props = message as Props;
                var m = props != null ? Context.ActorOf(props) : message;
                Sender.Tell(m);
                return true;
            }
        }

        internal class ResumingSupervisor : Supervisor
        {
            public ResumingSupervisor(IActorRef testActor) : base(testActor)
            {
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(exception =>
                {
                    TestActor.Tell(exception);
                    return Directive.Resume;
                });
            }
        }

        internal class FailingRecovery : PersistentActorSpec.ExamplePersistentActor
        {

            public FailingRecovery(string name) : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Persist(new Evt(cmd.Data), evt => UpdateState(evt));
                    }
                }
                else return false;
                return true;
            }

            private bool FailingRecover(object message)
            {
                if (message is Evt)
                {
                    if (((Evt) message).Data.Equals("bad"))
                        throw new SimulatedException("Simulated exception from receiveRecover");
                }
                return true;
            }

            protected override bool ReceiveRecover(object message)
            {
                if (!FailingRecover(message))
                {
                    return base.ReceiveRecover(message);
                }
                return true;
            }
        }

        internal class ThrowingActorOne : PersistentActorSpec.ExamplePersistentActor
        {
            public ThrowingActorOne(string name) : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Persist(new Evt(string.Format("{0}", cmd.Data)), evt => UpdateState(evt));
                        if (cmd.Data.Equals("err"))
                            throw new SimulatedException("simulated exception");
                    }
                }
                else return false;
                return true;
            }
        }

        internal class ThrowingActorTwo : PersistentActorSpec.ExamplePersistentActor
        {
            public ThrowingActorTwo(string name) : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Persist(new Evt(string.Format("{0}", cmd.Data)), evt =>
                        {
                            if (cmd.Data.Equals("err"))
                                throw new SimulatedException("simulated exception");
                            UpdateState(evt);
                        });
                    }
                }
                else return false;
                return true;
            }
        }

        #endregion

        public PersistentActorFailureSpec()
            : base(Configuration("PersistentActorFailureSpec",
                extraConfig:
                    @"akka.persistence.journal.inmem.class = ""Akka.Persistence.Tests.PersistentActorFailureSpec+FailingMemoryJournal, Akka.Persistence.Tests""
                    akka.actor.serialize-messages=off"))
        {
            Sys.EventStream.Publish(new Mute(new CustomEventFilter(e => e.Message is AskTimeoutException)));
            //TODO: remove akka.actor.serialize-messages=off when Props serialization will be resolved (github issue: #569)
        }

        private void PrepareFailingRecovery()
        {
            var pref = ActorOf(Props.Create(() => new FailingRecovery(Name)));
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("bad"));
            pref.Tell(new Cmd("c"));
            pref.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a", "b", "bad", "c"});
        }

        [Fact]
        public void PersistentActor_should_stop_if_recovery_from_persisted_events_fails()
        {
            var pref = ActorOf(Props.Create(() => new BehaviorOneActor(Name)));
            pref.Tell(new Cmd("corrupt"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("corrupt-1", "corrupt-2");

            // recover by creating another with same name
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(TestActor)));
            sup.Tell(new Props(typeof (BehaviorOneActor), new object[] {Name}));
            var newPref = ExpectMsg<IActorRef>();
            Watch(newPref);
            ExpectTerminated(newPref);
        }

        [Fact]
        public void PersistentActor_should_call_OnRecoveryFailure_when_recovery_from_persisted_events_fails()
        {
            var props = Props.Create(() => new OnRecoveryFailurePersistentActor(Name, TestActor));
            var pref = ActorOf(props);
            pref.Tell(new Cmd("corrupt"));
            pref.Tell(GetState.Instance);
            ExpectMsg(new object[] {"corrupt"});

            // recover by creating another with same name
            // note that if we used testActor as failure detector passed in
            // the props we'd have a race on our hands
            var failProbe = CreateTestProbe();
            var sameNameProps = Props.Create(() => new OnRecoveryFailurePersistentActor(Name, failProbe.Ref));
            Sys.ActorOf(Props.Create(() => new Supervisor(TestActor))).Tell(sameNameProps);
            var newPref = ExpectMsg<IActorRef>();
            failProbe.ExpectMsg("recovery-failure:blahonga 1 1");
            Watch(newPref);
            ExpectTerminated(newPref);
        }

        [Fact]
        public void PersistentActor_should_call_OnPersistFailure_and_stop_when_Persist_fails()
        {
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(TestActor)));
            sup.Tell(Props.Create(() => new BehaviorOneActor(Name)));
            var pref = ExpectMsg<IActorRef>();
            Watch(pref);
            pref.Tell(new Cmd("wrong"));
            ExpectMsg("Failure: wrong-1");
            ExpectTerminated(pref);
        }

        [Fact]
        public void PersistentActor_should_call_OnPersistFailure_and_stop_when_PersistAsync_fails()
        {
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(TestActor)));
            sup.Tell(Props.Create(() => new PersistentActorSpec.AsyncPersistActor(Name)));
            var pref = ExpectMsg<IActorRef>();
            pref.Tell(new Cmd("a"));
            Watch(pref);
            ExpectMsg("a"); // reply before PersistAsync
            ExpectMsg("a-1"); // reply after successful PersistAsync
            pref.Tell(new Cmd("wrong"));
            ExpectMsg("wrong"); // reply before PersistAsync
            ExpectMsg("Failure: wrong-2"); // OnPersistFailure sent message
            ExpectTerminated(pref);
        }

        [Fact]
        public void PersistentActor_should_call_OnPersistRejected_and_continue_if_Persist_rejected()
        {
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(TestActor)));
            sup.Tell(Props.Create(() => new BehaviorOneActor(Name)));
            var pref = ExpectMsg<IActorRef>();
            pref.Tell(new Cmd("not serializable"));
            ExpectMsg("Rejected: not serializable-1");
            ExpectMsg("Rejected: not serializable-2");

            pref.Tell(new Cmd("a"));
            pref.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a-1", "a-2"});
        }

        [Fact]
        public void PersistentActor_should_stop_if_ReceiveRecover_fails()
        {
            PrepareFailingRecovery();

            // recover by creating another with same name
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(TestActor)));
            sup.Tell(Props.Create(() => new FailingRecovery(Name)));
            var pref = ExpectMsg<IActorRef>();
            Watch(pref);
            ExpectTerminated(pref);
        }

        [Fact]
        public void PersistentActor_should_support_resume_when_Persist_followed_by_exception()
        {
            var sup = Sys.ActorOf(Props.Create(() => new ResumingSupervisor(TestActor)));
            sup.Tell(Props.Create(() => new ThrowingActorOne(Name)));
            var pref = ExpectMsg<IActorRef>();
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("err"));
            pref.Tell(new Cmd("b"));
            ExpectMsg<SimulatedException>(); // from supervisor
            pref.Tell(new Cmd("c"));
            pref.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a", "err", "b", "c"});
        }

        [Fact]
        public void PersistentActor_should_support_restart_when_Persist_followed_by_exception()
        {
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(TestActor)));
            sup.Tell(Props.Create(() => new ThrowingActorOne(Name)));
            var pref = ExpectMsg<IActorRef>();
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("err"));
            pref.Tell(new Cmd("b"));
            ExpectMsg<SimulatedException>(); // from supervisor
            pref.Tell(new Cmd("c"));
            pref.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a", "err", "b", "c"});
        }

        [Fact]
        public void PersistentActor_should_support_resume_when_Persist_handlers_throws_exception()
        {
            var sup = Sys.ActorOf(Props.Create(() => new ResumingSupervisor(TestActor)));
            sup.Tell(Props.Create(() => new ThrowingActorTwo(Name)));
            var pref = ExpectMsg<IActorRef>();
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("err"));
            pref.Tell(new Cmd("c"));
            ExpectMsg<SimulatedException>(); // from supervisor
            pref.Tell(new Cmd("d"));
            pref.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a", "b", "c", "d"});
        }

        [Fact]
        public void PersistentActor_should_support_restart_when_Persist_handlers_throws_exception()
        {
            var sup = Sys.ActorOf(Props.Create(() => new Supervisor(TestActor)));
            sup.Tell(Props.Create(() => new ThrowingActorTwo(Name)));
            var pref = ExpectMsg<IActorRef>();
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("err"));
            pref.Tell(new Cmd("c"));
            ExpectMsg<SimulatedException>(); // from supervisor
            pref.Tell(new Cmd("d"));
            pref.Tell(GetState.Instance);
            // err was stored and was replayed
            ExpectMsg(new object[] {"a", "b", "err", "c", "d"});
        }

        [Fact]
        public void PersistentActor_should_detect_overlapping_writers_during_replay()
        {
            var p1 = ActorOf(Props.Create(() => new BehaviorOneActor(Name)));
            p1.Tell(new Cmd("a"));
            p1.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a-1", "a-2"});

            // create another with same PersistenceId
            var p2 = ActorOf(Props.Create(() => new BehaviorOneActor(Name)));
            p2.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a-1", "a-2"});

            // continue writing from the old writer
            p1.Tell(new Cmd("b"));
            p1.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a-1", "a-2", "b-1", "b-2"});

            // write from the new writer
            p2.Tell(new Cmd("c"));
            p2.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a-1", "a-2", "c-1", "c-2"});

            // create yet another one with same PersistenceId, b-1 and b-2 discarded during replay
            var p3 = ActorOf(Props.Create(() => new BehaviorOneActor(Name)));
            p3.Tell(GetState.Instance);
            ExpectMsg(new object[] {"a-1", "a-2", "c-1", "c-2"});
        }
    }
}
