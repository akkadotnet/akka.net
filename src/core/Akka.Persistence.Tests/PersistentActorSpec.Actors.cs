using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;

namespace Akka.Persistence.Tests
{
    public partial class PersistentActorSpec
    {
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

        internal class LatchCmd
        {
            public LatchCmd(TestLatch latch, object data)
            {
                Latch = latch;
                Data = data;
            }

            public TestLatch Latch { get; private set; }
            public object Data { get; private set; }
        }

        internal abstract class ExamplePersistentActor : NamedPersistentActor
        {
            protected LinkedList<object> Events = new LinkedList<object>();

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
                {
                    Events.AddFirst((message as Evt).Data);
                    return true;
                }

                return false;
            }

            protected bool CommonBehavior(object message)
            {
                if (message is GetState) Sender.Tell(Events.Reverse().ToArray());
                else if (message.ToString() == "boom") throw new TestException("boom");
                else return false;
                return true;
            }
        }

        internal class BehaviorOneActor : ExamplePersistentActor
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
                    Persist(new[] { new Evt(cmd.Data + "-1"), new Evt(cmd.Data + "-2") }, UpdateStateHandler);
                    return true;
                }
                return false;
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
                    Persist(new[] { new Evt(cmd.Data + "-1"), new Evt(cmd.Data + "-2") }, UpdateStateHandler);
                    Persist(new[] { new Evt(cmd.Data + "-3"), new Evt(cmd.Data + "-4") }, UpdateStateHandler);
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
                    Persist(new[] { new Evt(cmd.Data + "-11"), new Evt(cmd.Data + "-12") }, UpdateStateHandler);
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
                        Context.Unbecome();
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
                        Context.Unbecome();
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
                    Context.Unbecome();
                    Persist(new[] { new Evt(cmd.Data + "-31"), new Evt(cmd.Data + "-32") }, UpdateStateHandler);
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
                    Persist(new[] { new Evt(cmd.Data + "-31"), new Evt(cmd.Data + "-32") }, UpdateStateHandler);
                    UpdateState(new Evt(cmd.Data + "-30"));
                    Context.Unbecome();
                    return true;
                }
                return false;
            }
        }

        internal class SnapshottingPersistentActor : ExamplePersistentActor
        {
            protected readonly ActorRef Probe;
            public SnapshottingPersistentActor(string name, ActorRef probe)
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
                        Events = (message as SnapshotOffer).Snapshot as LinkedList<object>;
                    }
                    else return false;
                }
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (CommonBehavior(message)) ;
                else if (message is Cmd) HandleCmd(message as Cmd);
                else if (message is SaveSnapshotSuccess) Probe.Tell("saved");
                else if (message.ToString() == "snap") SaveSnapshot(Events);
                else return false;
                return true;
            }

            private void HandleCmd(Cmd cmd)
            {
                Persist(new[] { new Evt(cmd.Data + "-41"), new Evt(cmd.Data + "-42") }, UpdateStateHandler);
            }
        }

        internal class SnapshottingBecomingPersistentActor : SnapshottingPersistentActor
        {
            public const string Message = "It's changing me";
            public const string Response = "I'm becoming";
            public SnapshottingBecomingPersistentActor(string name, ActorRef probe) : base(name, probe) { }

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
                if (ReceiveCommand(message)) ;
                else if (message.ToString() == Message) Probe.Tell(Response);
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

        internal class UserStashActor : ExamplePersistentActor
        {
            private bool _stashed = false;
            public UserStashActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    if (cmd.Data == "a")
                    {
                        if (!_stashed) Stash.Stash();
                        else Sender.Tell("a");

                    }
                    else if (cmd.Data == "b") Persist(new Evt("b"), evt => Sender.Tell(evt.Data));
                    else if (cmd.Data == "c")
                    {
                        UnstashAll();
                        Sender.Tell("c");
                    }
                    else return false;
                }
                else return false;
                return true;
            }
        }

        internal class UserStashManyActor : ExamplePersistentActor
        {
            public UserStashManyActor(string name)
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
                        if (cmd.Data == "a")
                        {
                            Persist(new Evt("a"), evt =>
                            {
                                UpdateState(evt);
                                Context.Become(ProcessC);
                            });
                        }
                        else if (cmd.Data == "b-1" || cmd.Data == "b-2")
                        {
                            Persist(new Evt(cmd.Data.ToString()), UpdateStateHandler);
                        }

                        return true;
                    }
                }
                else return true;
                return false;
            }

            protected bool ProcessC(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data == "c")
                {
                    Persist(new Evt("c"), evt =>
                    {
                        UpdateState(evt);
                        Context.Unbecome();
                    });
                    Stash.UnstashAll();
                }
                else Stash.Stash();
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
                        for (int i = 0; i < 3; i++)
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

        internal class UserStashFailureActor : ExamplePersistentActor
        {
            public UserStashFailureActor(string name)
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
                        if (cmd.Data.ToString() == "b-2") throw new TestException("boom");

                        Persist(new Evt(cmd.Data), evt =>
                        {
                            UpdateState(evt);
                            if (cmd.Data.ToString() == "a") Context.Become(OtherCommandHandler);
                        });

                        return true;
                    }
                }
                else return true;
                return false;
            }

            protected bool OtherCommandHandler(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "c")
                {
                    //FIXME: after persisting Evt(c) inner callback is never called during tests
                    // therefore no context unbecome occurs and all Cmd(b-X) leave unprocessed
                    Persist(new Evt("c"), evt =>
                    {
                        UpdateState(evt);
                        Context.Unbecome();
                    });
                    Stash.UnstashAll();
                }
                else Stash.Stash();
                return true;
            }
        }

        internal class IntEventPersistentActor : ExamplePersistentActor
        {
            public IntEventPersistentActor(string name)
                : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "a")
                {
                    Persist(5, i =>
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
            public HandleRecoveryFinishedEventPersistentActor(string name, ActorRef probe)
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
                    Defer("d-1", Sender.Tell);
                    Persist(cmd.Data + "-2", Sender.Tell);
                    Defer("d-3", Sender.Tell);
                    Defer("d-4", Sender.Tell);

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
                    Defer("d-" + cmd.Data + "-1", Sender.Tell);
                    PersistAsync("pa-" + cmd.Data + "-2", Sender.Tell);
                    Defer("d-" + cmd.Data + "-3", Sender.Tell);
                    Defer("d-" + cmd.Data + "-4", Sender.Tell);

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
                    Defer("d-" + cmd.Data + "-3", Sender.Tell);
                    Defer("d-" + cmd.Data + "-4", Sender.Tell);
                    PersistAsync("pa-" + cmd.Data + "-5", Sender.Tell);
                    Defer("d-" + cmd.Data + "-6", Sender.Tell);

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
                    Defer("d-1", Sender.Tell);
                    Defer("d-2", Sender.Tell);
                    Defer("d-3", Sender.Tell);

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
    }
}