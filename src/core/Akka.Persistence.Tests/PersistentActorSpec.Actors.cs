using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.TestKit;

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
        }

        internal class Evt
        {
            public Evt(object data)
            {
                Data = data;
            }

            public object Data { get; private set; }
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
                if (message is GetState) Sender.Tell(Events.Reverse());
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
    }
}