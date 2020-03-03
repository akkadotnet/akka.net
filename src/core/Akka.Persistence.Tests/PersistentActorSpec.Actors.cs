//-----------------------------------------------------------------------
// <copyright file="PersistentActorSpec.Actors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Akka.Persistence.Internal;

namespace Akka.Persistence.Tests
{
    internal class BehaviorOneActor : PersistentActorSpec.ExamplePersistentActor
    {
        public BehaviorOneActor(string name) : base(name) { }

        protected override bool ReceiveCommand(object message)
        {
            return CommonBehavior(message) || Receiver(message);
        }

        protected bool Receiver(object message)
        {
            if (message is Cmd)
            {
                var cmd = message as Cmd;
                PersistAll(new[] { new Evt(cmd.Data + "-1"), new Evt(cmd.Data + "-2") }, UpdateStateHandler);
            }
            else if (message is DeleteMessagesSuccess)
            {
                if (AskedForDelete == null)
                    throw new ArgumentNullException("Received DeleteMessagesSuccess without anyone asking for delete!");
                AskedForDelete.Tell(message);
            }
            else return false;
            return true;
        }

        protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
        {
            if (@event is Evt)
                Sender.Tell("Rejected: " + ((Evt)@event).Data);
            else
                base.OnPersistRejected(cause, @event, sequenceNr);
        }

        protected override void OnPersistFailure(Exception cause, object @event, long sequenceNr)
        {
            if (@event is Evt)
                Sender.Tell("Failure: " + ((Evt)@event).Data);
            else
                base.OnPersistFailure(cause, @event, sequenceNr);
        }
    }
    internal class Cmd
    {
        public Cmd(object data)
        {
            Data = data;
        }

        public object Data { get; private set; }

        public override string ToString()
        {
            return "Cmd(" + Data + ")";
        }
    }

    internal class Evt
    {
        public Evt(object data)
        {
            Data = data;
        }

        public object Data { get; private set; }

        public override string ToString()
        {
            return "Evt(" + Data + ")";
        }
    }

    public partial class PersistentActorSpec
    {


        internal class LatchCmd : INoSerializationVerificationNeeded
        {
            public LatchCmd(TestLatch latch, object data)
            {
                Latch = latch;
                Data = data;
            }

            public TestLatch Latch { get; private set; }
            public object Data { get; private set; }
        }

        internal class Delete
        {
            public Delete(long toSequenceNr)
            {
                ToSequenceNr = toSequenceNr;
            }

            public long ToSequenceNr { get; private set; }

            public override string ToString()
            {
                return "Delete(" + ToSequenceNr + ")";
            }
        }


        internal abstract class ExamplePersistentActor : NamedPersistentActor
        {
            protected ImmutableArray<object> Events = ImmutableArray<object>.Empty;
            protected IActorRef AskedForDelete;

            protected readonly Action<object> UpdateStateHandler;

            protected ExamplePersistentActor(string name)
                : base(name)
            {
                UpdateStateHandler = o => UpdateState(o);
            }

            protected override bool ReceiveRecover(object message)
            {
                return UpdateState(message);
            }

            protected bool UpdateState(object message)
            {
                if (message is Evt)
                    Events = Events.AddFirst((message as Evt).Data);
                else if (message is IActorRef)
                    AskedForDelete = (IActorRef) message;
                else
                    return false;
                return true;
            }

            protected bool CommonBehavior(object message)
            {
                if (message is GetState) Sender.Tell(Events.Reverse().ToArray());
                else if (message.ToString() == "boom") throw new TestException("boom");
                else if (message is Delete)
                {
                    Persist(Sender, s => AskedForDelete = s);
                    DeleteMessages(((Delete)message).ToSequenceNr);
                }
                else return false;
                return true;
            }
        }


        internal class BehaviorTwoActor : ExamplePersistentActor
        {
            public BehaviorTwoActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                return CommonBehavior(message) || Receiver(message);
            }

            protected bool Receiver(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    PersistAll(new[] { new Evt(cmd.Data + "-1"), new Evt(cmd.Data + "-2") }, UpdateStateHandler);
                    PersistAll(new[] { new Evt(cmd.Data + "-3"), new Evt(cmd.Data + "-4") }, UpdateStateHandler);
                    return true;
                }
                return false;
            }
        }
        internal class BehaviorThreeActor : ExamplePersistentActor
        {
            public BehaviorThreeActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                return CommonBehavior(message) || Receiver(message);
            }

            protected bool Receiver(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    PersistAll(new[] { new Evt(cmd.Data + "-11"), new Evt(cmd.Data + "-12") }, UpdateStateHandler);
                    UpdateState(new Evt(cmd.Data + "-10"));
                    return true;
                }
                return false;
            }
        }

        internal class ChangeBehaviorInLastEventHandlerActor : ExamplePersistentActor
        {
            public ChangeBehaviorInLastEventHandlerActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                if (CommonBehavior(message)) return true;

                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    Persist(new Evt(cmd.Data + "-0"), evt =>
                    {
                        UpdateState(evt);
                        Context.Become(NewBehavior);
                    });
                    return true;
                }
                return false;
            }

            protected bool NewBehavior(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;

                    Persist(new Evt(cmd.Data + "-21"), UpdateStateHandler);
                    Persist(new Evt(cmd.Data + "-22"), evt =>
                    {
                        UpdateState(evt);
                        Context.UnbecomeStacked();
                    });
                    return true;
                }
                return false;
            }
        }

        internal class ChangeBehaviorInFirstEventHandlerActor : ExamplePersistentActor
        {
            public ChangeBehaviorInFirstEventHandlerActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                if (CommonBehavior(message)) return true;

                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    Persist(new Evt(cmd.Data + "-0"), evt =>
                    {
                        UpdateState(evt);
                        Context.Become(NewBehavior);
                    });
                    return true;
                }
                return false;
            }

            protected bool NewBehavior(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;

                    Persist(new Evt(cmd.Data + "-21"), evt =>
                    {
                        UpdateState(evt);
                        Context.UnbecomeStacked();
                    });
                    Persist(new Evt(cmd.Data + "-22"), UpdateStateHandler);
                    return true;
                }
                return false;
            }
        }
        internal class ChangeBehaviorInCommandHandlerFirstActor : ExamplePersistentActor
        {
            public ChangeBehaviorInCommandHandlerFirstActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                if (CommonBehavior(message)) return true;
                
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    Context.Become(NewBehavior);
                    Persist(new Evt(cmd.Data + "-0"), UpdateStateHandler);
                    return true;
                }
                return false;
            }

            protected bool NewBehavior(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    Context.UnbecomeStacked();
                    PersistAll(new[] { new Evt(cmd.Data + "-31"), new Evt(cmd.Data + "-32") }, UpdateStateHandler);
                    UpdateState(new Evt(cmd.Data + "-30"));
                    return true;
                }
                return false;
            }
        }

        internal class ChangeBehaviorInCommandHandlerLastActor : ExamplePersistentActor
        {
            public ChangeBehaviorInCommandHandlerLastActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                if (CommonBehavior(message)) return true;
                
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    Persist(new Evt(cmd.Data + "-0"), UpdateStateHandler);
                    Context.Become(NewBehavior);
                    return true;
                }
                return false;
            }

            protected bool NewBehavior(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    PersistAll(new[] { new Evt(cmd.Data + "-31"), new Evt(cmd.Data + "-32") }, UpdateStateHandler);
                    UpdateState(new Evt(cmd.Data + "-30"));
                    Context.UnbecomeStacked();
                    return true;
                }
                return false;
            }
        }

        internal class SnapshottingPersistentActor : ExamplePersistentActor
        {
            protected readonly IActorRef Probe;
            public SnapshottingPersistentActor(string name, IActorRef probe)
                : base(name)
            {
                Probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                if (!base.ReceiveRecover(message))
                {
                    if (message is SnapshotOffer)
                    {
                        Probe.Tell("offered");
                        Events = (message as SnapshotOffer).Snapshot.AsInstanceOf<ImmutableArray<object>>();
                    }
                    else return false;
                }
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (CommonBehavior(message)) return true;
                if (message is Cmd) HandleCmd(message as Cmd);
                else if (message is SaveSnapshotSuccess) Probe.Tell("saved");
                else if (message.ToString() == "snap") SaveSnapshot(Events);
                else return false;
                return true;
            }

            private void HandleCmd(Cmd cmd)
            {
                PersistAll(new[] { new Evt(cmd.Data + "-41"), new Evt(cmd.Data + "-42") }, UpdateStateHandler);
            }
        }

        internal class SnapshottingBecomingPersistentActor : SnapshottingPersistentActor
        {
            public const string Message = "It's changing me";
            public const string Response = "I'm becoming";
            public SnapshottingBecomingPersistentActor(string name, IActorRef probe) : base(name, probe) { }

            private bool BecomingRecover(object message)
            {
                if (message is SnapshotOffer)
                {
                    Context.Become(BecomingCommand);
                    // sending ourself a normal message here also tests
                    // that we stash them until recovery is complete
                    Self.Tell(Message);
                    return base.ReceiveRecover(message);
                }
                return false;
            }

            protected override bool ReceiveRecover(object message)
            {
                return BecomingRecover(message) || base.ReceiveRecover(message);
            }

            private bool BecomingCommand(object message)
            {
                if (ReceiveCommand(message)) return true;
                if (message.ToString() == Message) Probe.Tell(Response);
                else return false;
                return true;
            }
        }

        internal class ReplyInEventHandlerActor : ExamplePersistentActor
        {
            public ReplyInEventHandlerActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                if (message is Cmd) Persist(new Evt("a"), evt => Sender.Tell(evt.Data));
                else return false;
                return true;
            }
        }

        internal class AsyncPersistActor : ExamplePersistentActor
        {
            private int _counter = 0;
            public AsyncPersistActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Sender.Tell(cmd.Data);
                        PersistAsync(new Evt(cmd.Data.ToString() + "-" + (++_counter)), evt =>
                        {
                            Sender.Tell(evt.Data);
                        });

                        return true;
                    }
                }
                else return true;
                return false;
            }

            protected override void OnPersistFailure(Exception cause, object @event, long sequenceNr)
            {
                if (@event is Evt)
                    Sender.Tell(string.Format("Failure: {0}", ((Evt)@event).Data));
                else
                    base.OnPersistFailure(cause, @event, sequenceNr);
            }
        }

        internal class AsyncPersistThreeTimesActor : ExamplePersistentActor
        {
            private int _counter = 0;
            public AsyncPersistThreeTimesActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Sender.Tell(cmd.Data);
                        for (int i = 1; i <= 3; i++)
                        {
                            PersistAsync(new Evt(cmd.Data.ToString() + "-" + (++_counter)), evt =>
                            {
                                Sender.Tell("a" + evt.Data.ToString().Substring(1));
                            });
                        }

                        return true;
                    }
                }
                else return true;
                return false;
            }
        }

        internal class AsyncPersistSameEventTwiceActor : ExamplePersistentActor
        {
            private AtomicCounter _sendMessageCounter = new AtomicCounter(0);
            public AsyncPersistSameEventTwiceActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Sender.Tell(cmd.Data);
                        var @event = new Evt(cmd.Data);

                        PersistAsync(@event, evt =>
                        {
                            Thread.Sleep(300);
                            Sender.Tell(evt.Data.ToString() + "-a-" + _sendMessageCounter.IncrementAndGet());
                        });

                        PersistAsync(@event, evt => Sender.Tell(evt.Data.ToString() + "-b-" + _sendMessageCounter.IncrementAndGet()));

                        return true;
                    }
                }
                else return true;
                return false;
            }
        }

        internal class PersistAllNullActor : ExamplePersistentActor
        {
            public PersistAllNullActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        var data = (string)cmd.Data;
                        if (data.Contains("defer"))
                        {
                            DeferAsync("before-nil", evt => Sender.Tell(evt));
                            PersistAll<object>(null, evt => Sender.Tell("Null"));
                            DeferAsync("after-nil", evt => Sender.Tell(evt));
                            Sender.Tell(data);
                        }
                        else if (data.Contains("persist"))
                        {
                            Persist("before-nil", evt => Sender.Tell(evt));
                            PersistAll<object>(null, evt => Sender.Tell("Null"));
                            DeferAsync("after-nil", evt => Sender.Tell(evt));
                            Sender.Tell(data);
                            return true;
                        }

                        return true;
                    }
                }
                else return true;
                return false;
            }
        }

        internal class AsyncPersistAndPersistMixedSyncAsyncSyncActor : ExamplePersistentActor
        {
            private int _counter = 0;
            public AsyncPersistAndPersistMixedSyncAsyncSyncActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Sender.Tell(cmd.Data);

                        Persist(new Evt(cmd.Data + "-e1"), evt => Sender.Tell(evt.Data + "-" + (++_counter)));
                        PersistAsync(new Evt(cmd.Data + "-ea2"), evt => Sender.Tell(evt.Data + "-" + (++_counter)));
                        Persist(new Evt(cmd.Data + "-e3"), evt => Sender.Tell(evt.Data + "-" + (++_counter)));

                        return true;
                    }
                }
                else return true;
                return false;
            }
        }

        internal class AsyncPersistAndPersistMixedSyncAsyncActor : ExamplePersistentActor
        {
            private int _counter = 0;
            
            public AsyncPersistAndPersistMixedSyncAsyncActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        Sender.Tell(cmd.Data);

                        Persist(new Evt(cmd.Data + "-e1"), evt => Sender.Tell(evt.Data + "-" + (++_counter)));
                        PersistAsync(new Evt(cmd.Data + "-ea2"), evt => Sender.Tell(evt.Data + "-" + (++_counter)));

                        return true;
                    }
                }
                else return true;
                return false;
            }
        }

        internal class AsyncPersistHandlerCorrelationCheck : ExamplePersistentActor
        {
            private int _counter = 0;
            public AsyncPersistHandlerCorrelationCheck(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message))
                {
                    var cmd = message as Cmd;
                    if (cmd != null)
                    {
                        PersistAsync(new Evt(cmd.Data), evt =>
                        {
                            if (!cmd.Data.Equals(evt.Data)) Sender.Tell("Expected " + cmd.Data + " but got " + evt.Data);
                            if ("done" != evt.Data.ToString()) Sender.Tell("done");
                        });

                        return true;
                    }
                }
                else return true;
                return false;
            }
        }

        internal class ValueTypeEventPersistentActor : ExamplePersistentActor
        {
            public ValueTypeEventPersistentActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "a")
                {
                    Persist(5L, i =>
                    {
                        Sender.Tell(i);
                    });
                    return true;
                }
                else return false;
            }
        }

        internal class HandleRecoveryFinishedEventPersistentActor : SnapshottingPersistentActor
        {
            public HandleRecoveryFinishedEventPersistentActor(string name, IActorRef probe)
                : base(name, probe)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!base.ReceiveCommand(message)) Probe.Tell(message.ToString());
                return true;
            }

            protected override bool ReceiveRecover(object message)
            {
                return SendingRecover(message) || base.ReceiveRecover(message);
            }

            protected bool SendingRecover(object message)
            {
                if (message is SnapshotOffer)
                {
                    // sending ourself a normal message tests
                    // that we stash them until recovery is complete
                    Self.Tell("I am the stashed");
                    base.ReceiveRecover(message);
                }
                else if (message is RecoveryCompleted)
                {
                    Probe.Tell(RecoveryCompleted.Instance);
                    Self.Tell("I am the recovered");
                    UpdateState(new Evt(RecoveryCompleted.Instance));
                }
                else return false;
                return true;
            }
        }

        internal class DeferringWithPersistActor : ExamplePersistentActor
        {
            public DeferringWithPersistActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null)
                {
                    DeferAsync("d-1", Sender.Tell);
                    Persist(cmd.Data + "-2", Sender.Tell);
                    DeferAsync("d-3", Sender.Tell);
                    DeferAsync("d-4", Sender.Tell);

                    return true;
                }
                return false;
            }
        }

        internal class DeferringWithAsyncPersistActor : ExamplePersistentActor
        {
            public DeferringWithAsyncPersistActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null)
                {
                    DeferAsync("d-" + cmd.Data + "-1", Sender.Tell);
                    PersistAsync("pa-" + cmd.Data + "-2", Sender.Tell);
                    DeferAsync("d-" + cmd.Data + "-3", Sender.Tell);
                    DeferAsync("d-" + cmd.Data + "-4", Sender.Tell);

                    return true;
                }
                return false;
            }
        }

        internal class DeferringMixedCallsPPADDPADPersistActor : ExamplePersistentActor
        {
            public DeferringMixedCallsPPADDPADPersistActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null)
                {
                    Persist("p-" + cmd.Data + "-1", Sender.Tell);
                    PersistAsync("pa-" + cmd.Data + "-2", Sender.Tell);
                    DeferAsync("d-" + cmd.Data + "-3", Sender.Tell);
                    DeferAsync("d-" + cmd.Data + "-4", Sender.Tell);
                    PersistAsync("pa-" + cmd.Data + "-5", Sender.Tell);
                    DeferAsync("d-" + cmd.Data + "-6", Sender.Tell);

                    return true;
                }
                return false;
            }
        }

        internal class DeferringWithNoPersistCallsPersistActor : ExamplePersistentActor
        {
            public DeferringWithNoPersistCallsPersistActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null)
                {
                    DeferAsync("d-1", Sender.Tell);
                    DeferAsync("d-2", Sender.Tell);
                    DeferAsync("d-3", Sender.Tell);

                    return true;
                }
                return false;
            }
        }

        internal class StressOrdering : ExamplePersistentActor
        {
            public StressOrdering(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is LatchCmd)
                {
                    var latchCmd = message as LatchCmd;
                    Sender.Tell(latchCmd.Data);
                    latchCmd.Latch.Ready(TimeSpan.FromSeconds(5));
                    PersistAsync(latchCmd.Data, _ => { });
                }
                else if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    Sender.Tell(cmd.Data);
                    PersistAsync(cmd.Data, _ => { });
                }
                else if (message is string)
                    Sender.Tell(message.ToString());
                else return false;
                return true;
            }
        }

        internal class RecoverMessageCausedRestart : ExamplePersistentActor
        {
            private IActorRef _master;
            public RecoverMessageCausedRestart(string name) : base(name)
            {

            }

            protected override bool ReceiveCommand(object message)
            {
                if (message.Equals("boom"))
                {
                    _master = Sender;
                    throw new TestException("boom");
                }
                return false;
            }

            protected override void PreRestart(Exception reason, object message)
            {
                _master?.Tell($"failed with {reason.GetType().Name} while processing {message}");
                Context.Stop(Self);
            }
        }

        internal class MultipleAndNestedPersists : ExamplePersistentActor
        {
            private readonly IActorRef _probe;

            public MultipleAndNestedPersists(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    var s = (string) message;
                    _probe.Tell(s);
                    Persist(s + "-outer-1", outer =>
                    {
                        _probe.Tell(outer);
                        Persist(s + "-inner-1", inner => _probe.Tell(inner));
                    });
                    Persist(s + "-outer-2", outer =>
                    {
                        _probe.Tell(outer);
                        Persist(s + "-inner-2", inner => _probe.Tell(inner));
                    });
                    return true;
                }
                return false;
            }
        }

        internal class MultipleAndNestedPersistAsyncs : ExamplePersistentActor
        {
            private readonly IActorRef _probe;

            public MultipleAndNestedPersistAsyncs(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    var s = (string) message;
                    _probe.Tell(s);
                    PersistAsync(s + "-outer-1", outer =>
                    {
                        _probe.Tell(outer);
                        PersistAsync(s + "-inner-1", inner => _probe.Tell(inner));
                    });
                    PersistAsync(s + "-outer-2", outer =>
                    {
                        _probe.Tell(outer);
                        PersistAsync(s + "-inner-2", inner => _probe.Tell(inner));
                    });
                    return true;
                }
                return false;
            }
        }

        internal class NestedPersistNormalAndAsyncs : ExamplePersistentActor
        {
            private readonly IActorRef _probe;

            public NestedPersistNormalAndAsyncs(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    var s = (string) message;
                    _probe.Tell(s);
                    Persist(s + "-outer-1", outer =>
                    {
                        _probe.Tell(outer);
                        PersistAsync(s + "-inner-async-1", inner => _probe.Tell(inner));
                    });
                    Persist(s + "-outer-2", outer =>
                    {
                        _probe.Tell(outer);
                        PersistAsync(s + "-inner-async-2", inner => _probe.Tell(inner));
                    });
                    return true;
                }
                return false;
            }
        }

        internal class NestedPersistAsyncsAndNormal : ExamplePersistentActor
        {
            private readonly IActorRef _probe;

            public NestedPersistAsyncsAndNormal(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    var s = (string) message;
                    _probe.Tell(s);
                    PersistAsync(s + "-outer-async-1", outer =>
                    {
                        _probe.Tell(outer);
                        Persist(s + "-inner-1", inner => _probe.Tell(inner));
                    });
                    PersistAsync(s + "-outer-async-2", outer =>
                    {
                        _probe.Tell(outer);
                        Persist(s + "-inner-2", inner => _probe.Tell(inner));
                    });
                    return true;
                }
                return false;
            }
        }

        internal class NestedPersistInAsyncEnforcesStashing : ExamplePersistentActor
        {
            private readonly IActorRef _probe;

            public NestedPersistInAsyncEnforcesStashing(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    var s = (string) message;
                    _probe.Tell(s);
                    PersistAsync(s + "-outer-async", outer =>
                    {
                        _probe.Tell(outer);
                        Persist(s + "-inner", inner =>
                        {
                            _probe.Tell(inner);
                            Thread.Sleep(1000); // really long wait here
                            // the next incoming command must be handled by the following function
                            Context.Become(_ =>
                            {
                                Sender.Tell("done");
                                return true;
                            });
                        });
                    });
                    return true;
                }
                return false;
            }
        }

        internal class DeeplyNestedPersists : ExamplePersistentActor
        {
            private readonly int _maxDepth;
            private readonly IActorRef _probe;
            private readonly Dictionary<string, int> _currentDepths = new Dictionary<string, int>();

            public DeeplyNestedPersists(string name, int maxDepth, IActorRef probe)
                : base(name)
            {
                _maxDepth = maxDepth;
                _probe = probe;
            }

            private void WeMustGoDeeper(string dWithDepth)
            {
                var d = dWithDepth.Split('-')[0];
                _probe.Tell(dWithDepth);
                if (!_currentDepths.TryGetValue(d, out int currentDepth))
                    currentDepth = 1;
                if (currentDepth < _maxDepth)
                {
                    _currentDepths[d] = currentDepth + 1;
                    Persist(d + "-" + _currentDepths[d], WeMustGoDeeper);
                }
                else
                {
                    // reset depth counter before next command
                    _currentDepths[d] = 1;
                }
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    var s = (string) message;
                    _probe.Tell(s);
                    Persist(s + "-1", WeMustGoDeeper);
                    return true;
                }
                return false;
            }
        }

        internal class DeeplyNestedPersistAsyncs : ExamplePersistentActor
        {
            private readonly int _maxDepth;
            private readonly IActorRef _probe;
            private readonly Dictionary<string, int> _currentDepths = new Dictionary<string, int>();

            public DeeplyNestedPersistAsyncs(string name, int maxDepth, IActorRef probe)
                : base(name)
            {
                _maxDepth = maxDepth;
                _probe = probe;
            }

            private void WeMustGoDeeper(string dWithDepth)
            {
                var d = dWithDepth.Split('-')[0];
                _probe.Tell(dWithDepth);
                if (!_currentDepths.TryGetValue(d, out int currentDepth))
                    currentDepth = 1;
                if (currentDepth < _maxDepth)
                {
                    _currentDepths[d] = currentDepth + 1;
                    PersistAsync(d + "-" + _currentDepths[d], WeMustGoDeeper);
                }
                else
                {
                    // reset depth counter before next command
                    _currentDepths[d] = 1;
                }
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    var s = (string) message;
                    _probe.Tell(s);
                    PersistAsync(s + "-1", WeMustGoDeeper);
                    return true;
                }
                return false;
            }
        }

        internal class PersistInRecovery : ExamplePersistentActor
        {
            public PersistInRecovery(string name)
                : base(name)
            { }

            protected override bool ReceiveRecover(object message)
            {
                switch (message)
                {
                    case Evt evt when evt.Data?.ToString() == "invalid":
                        Persist(new Evt("invalid-recovery"), UpdateStateHandler);
                        return true;
                    case Evt evt:
                        return UpdateState(evt);
                    case RecoveryCompleted _:
                        PersistAsync(new Evt("rc-1"), UpdateStateHandler);
                        Persist(new Evt("rc-2"), UpdateStateHandler);
                        PersistAsync(new Evt("rc-3"), UpdateStateHandler);
                        return true;
                }

                return false;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (CommonBehavior(message)) return true;

                if (message is Cmd cmd)
                {
                    Persist(new Evt(cmd.Data), UpdateStateHandler);
                    return true;
                }

                return false;
            }
        }
    }
}

