using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence.Tests
{
    public partial class PersistentActorSpec
    {

        struct Cmd
        {
            public Cmd(object data)
                : this()
            {
                Data = data;
            }

            public object Data { get; private set; }
        }
        struct Evt
        {
            public Evt(object data)
                : this()
            {
                Data = data;
            }

            public object Data { get; private set; }
        }

        abstract class ExamplePersistentActor : PersistentActorBase
        {
            protected Receive ReceiveCommand;
            protected readonly Receive UpdateState;
            protected readonly Receive CommonBehavior;

            protected ExamplePersistentActor(string name)
                : base(name)
            {
                Events = new List<object>();
                UpdateState = msg =>
                {
                    if (msg is Evt)
                    {
                        var evt = (Evt)msg;
                        Events.Add(evt.Data);
                        return true;
                    }

                    return false;
                };
                CommonBehavior = msg =>
                {
                    if (msg == "boom")
                    {
                        throw new TestException("boom");
                    }
                    else if (msg is GetState)
                    {
                        // don't reverse the order, they are already pushed back
                        Sender.Tell(Events);
                    }
                    else return false;
                    return true;
                };
            }
            public List<object> Events { get; set; }
        }

        class Behavior1Processor : ExamplePersistentActor
        {
            public Behavior1Processor(string name)
                : base(name)
            {
                ReceiveCommand = msg =>
                {
                    if (!CommonBehavior(msg))
                    {
                        if (msg is Cmd)
                        {
                            var data = ((Cmd)msg).Data;
                            Persist(new List<object>(new object[]
                            {
                                new Evt(data.ToString()+"-1"),
                                new Evt(data.ToString()+"-2")
                            }), evt => UpdateState(evt));
                        }

                        return false;
                    }

                    return true;
                };
            }
        }

        class Behavior2Processor : ExamplePersistentActor
        {
            public Behavior2Processor(string name)
                : base(name)
            {
                ReceiveCommand = msg =>
                {
                    if (!CommonBehavior(msg))
                    {
                        if (msg is Cmd)
                        {
                            var data = ((Cmd)msg).Data.ToString();
                            Persist(new List<object>(new object[]
                            {
                                new Evt(data+"-1"),
                                new Evt(data+"-2")
                            }), evt => UpdateState(evt));
                            Persist(new List<object>(new object[]
                            {
                                new Evt(data+"-3"),
                                new Evt(data+"-4")
                            }), evt => UpdateState(evt));
                        }

                        return false;
                    }

                    return true;
                };
            }
        }
        class Behavior3Processor : ExamplePersistentActor
        {
            public Behavior3Processor(string name)
                : base(name)
            {
                ReceiveCommand = msg =>
                {
                    if (!CommonBehavior(msg))
                    {
                        if (msg is Cmd)
                        {
                            var data = ((Cmd)msg).Data.ToString();
                            Persist(new List<object>(new object[]
                            {
                                new Evt(data+"-11"),
                                new Evt(data+"-12")
                            }), evt => UpdateState(evt));
                            UpdateState(new Evt(data + "10"));
                        }

                        return false;
                    }

                    return true;
                };
            }
        }

        class ChangeBehaviorInFirstEventHandlerProcessor : ExamplePersistentActor
        {
            private readonly Receive NewBehavior;
            public ChangeBehaviorInFirstEventHandlerProcessor(string name)
                : base(name)
            {
                NewBehavior = msg =>
                {
                    if (msg is Cmd)
                    {
                        var dataStr = ((Cmd)msg).Data.ToString();
                        Persist(new Evt(dataStr + "-21"), evt =>
                        {
                            UpdateState(evt);
                            Context.Unbecome();
                        });
                        Persist(new Evt(dataStr + "-22"), evt => UpdateState(evt));
                        return true;
                    }

                    return false;
                };

                ReceiveCommand = msg =>
                {
                    if (!CommonBehavior(msg))
                    {
                        if (msg is Cmd)
                        {
                            var data = ((Cmd)msg).Data.ToString();
                            Persist(new Evt(data + "-0"), evt =>
                            {
                                UpdateState(evt);
                                Context.Become(NewBehavior);
                            });
                        }

                        return false;
                    }

                    return true;
                };
            }
        }
        class ChangeBehaviorInCommandHandlerFirstProcessor : ExamplePersistentActor
        {
            private readonly Receive NewBehavior;
            public ChangeBehaviorInCommandHandlerFirstProcessor(string name)
                : base(name)
            {
                NewBehavior = msg =>
                {
                    if (msg is Cmd)
                    {
                        var data = ((Cmd)msg).Data.ToString();
                        Context.Unbecome();
                        Persist(new List<object>(new object[] { new Evt(data + "-31"), new Evt(data + "-32") }), evt => UpdateState(evt));
                        UpdateState(new Evt(data + "30"));

                        return true;
                    }

                    return false;
                };

                ReceiveCommand = msg =>
                {
                    if (!CommonBehavior(msg))
                    {
                        if (msg is Cmd)
                        {
                            var data = ((Cmd)msg).Data.ToString();
                            Context.Become(NewBehavior, false);
                            Persist(new Evt(data + "-0"), evt => UpdateState(evt));
                        }

                        return false;
                    }

                    return true;
                };
            }
        }

        internal class TestException : Exception
        {
            public TestException(string boom)
                : base(boom)
            {
            }
        }
    }
}