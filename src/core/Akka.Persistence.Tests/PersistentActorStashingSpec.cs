//-----------------------------------------------------------------------
// <copyright file="PersistentActorStashingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Tests.Journal;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class PersistentActorStashingSpec : PersistenceSpec
    {
        internal abstract class StashExamplePersistentActor : PersistentActorSpec.ExamplePersistentActor
        {
            public StashExamplePersistentActor(string name) : base(name)
            {
            }

            protected virtual bool UnstashBehavior(object message)
            {
                return false;
            }
        }

        internal class UserStashActor : StashExamplePersistentActor
        {
            private bool _stashed = false;
            public UserStashActor(string name) : base(name) { }

            protected override bool ReceiveCommand(object message)
            {
                if (!UnstashBehavior(message))
                {
                    if (message is Cmd)
                    {
                        var cmd = message as Cmd;
                        if (cmd.Data.ToString() == "a")
                        {
                            if (!_stashed)
                            {
                                Stash.Stash();
                                _stashed = true;
                            }
                            else Sender.Tell("a");

                        }
                        else if (cmd.Data.ToString() == "b") Persist(new Evt("b"), evt => Sender.Tell(evt.Data));
                        else if (cmd.Data.ToString() == "c")
                        {
                            Stash.UnstashAll();
                            Sender.Tell("c");
                        }
                        else return false;
                    }
                    else return false;
                }
                return true;
            }

            protected override bool UnstashBehavior(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    if (cmd.Data.ToString() == "c")
                    {
                        Stash.UnstashAll();
                        Sender.Tell("c");
                    }
                    else return false;
                }
                else return false;
                return true;
            }
        }

        internal class UserStashWithinHandlerActor : UserStashActor
        {
            public UserStashWithinHandlerActor(string name) : base(name)
            {
            }

            protected override bool UnstashBehavior(object message)
            {
                if (message is Cmd)
                {
                    var cmd = message as Cmd;
                    if (cmd.Data.ToString() == "c")
                    {
                        Persist(new Evt("c"), e =>
                        {
                            Sender.Tell(e.Data);
                            Stash.UnstashAll();
                        });
                    }
                    else return false;
                }
                else return false;
                return true;
            }
        }

        internal class UserStashManyActor : StashExamplePersistentActor
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
                        var data = cmd.Data.ToString();
                        if (data == "a")
                        {
                            Persist(new Evt("a"), evt =>
                            {
                                UpdateState(evt);
                                Context.Become(ProcessC);
                            });
                        }
                        else if (data == "b-1" || data == "b-2")
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
                if (!UnstashBehavior(message))
                {
                    Stash.Stash();
                }
                return true;
            }

            protected override bool UnstashBehavior(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "c")
                {
                    Persist(new Evt("c"), evt =>
                    {
                        UpdateState(evt);
                        Context.UnbecomeStacked();
                    });
                    Stash.UnstashAll();
                }
                else return false;
                return true;
            }
        }

        internal class UserStashWithinHandlerManyActor : UserStashManyActor
        {
            public UserStashWithinHandlerManyActor(string name) : base(name)
            {
            }

            protected override bool UnstashBehavior(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "c")
                {
                    Persist(new Evt("c"), evt =>
                    {
                        UpdateState(evt);
                        Context.UnbecomeStacked();
                        Stash.UnstashAll();
                    });
                }
                else return false;
                return true;
            }
        }

        internal class UserStashFailureActor : StashExamplePersistentActor
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
                if (!UnstashBehavior(message))
                {
                    Stash.Stash();
                }
                return true;
            }

            protected override bool UnstashBehavior(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "c")
                {
                    Persist(new Evt("c"), evt =>
                    {
                        UpdateState(evt);
                        Context.UnbecomeStacked();
                    });
                    Stash.UnstashAll();
                }
                else return false;
                return true;
            }
        }

        internal class UserStashWithinHandlerFailureCallbackActor : UserStashFailureActor
        {
            public UserStashWithinHandlerFailureCallbackActor(string name) : base(name)
            {
            }

            protected override bool UnstashBehavior(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "c")
                {
                    Persist(new Evt("c"), evt =>
                    {
                        UpdateState(evt);
                        Context.UnbecomeStacked();
                        Stash.UnstashAll();
                    });
                }
                else return false;
                return true;
            }
        }

        public PersistentActorStashingSpec()
            : base(Configuration("PersistentActorStashingSpec"))
        {
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations()
        {
            var pref = ActorOf(Props.Create(() => new UserStashActor(Name)));
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));
            ExpectMsg("b");
            ExpectMsg("c");
            ExpectMsg("a");
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_within_handler()
        {
            var pref = ActorOf(Props.Create(() => new UserStashWithinHandlerActor(Name)));
            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));
            pref.Tell(new Cmd("c"));
            ExpectMsg("b");
            ExpectMsg("c");
            ExpectMsg("a");
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_with_several_stashed_messages()
        {
            var pref = ActorOf(Props.Create(() => new UserStashManyActor(Name)));
            var n = 10;
            var commands = Enumerable.Range(1, n).SelectMany(_ => new[] { new Cmd("a"), new Cmd("b-1"), new Cmd("b-2"), new Cmd("c"), });
            var evts = Enumerable.Range(1, n).SelectMany(_ => new[] { "a", "c", "b-1", "b-2" }).ToArray();

            foreach (var command in commands)
            {
                pref.Tell(command);
            }

            pref.Tell(GetState.Instance);

            ExpectMsgInOrder(evts);
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_within_handler_with_several_stashed_messages()
        {
            var pref = ActorOf(Props.Create(() => new UserStashWithinHandlerManyActor(Name)));
            var n = 10;
            var commands = Enumerable.Range(1, n).SelectMany(_ => new[] { new Cmd("a"), new Cmd("b-1"), new Cmd("b-2"), new Cmd("c"), });
            var evts = Enumerable.Range(1, n).SelectMany(_ => new[] { "a", "c", "b-1", "b-2" }).ToArray();

            foreach (var command in commands)
            {
                pref.Tell(command);
            }

            pref.Tell(GetState.Instance);

            ExpectMsgInOrder(evts);
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_under_failures()
        {
            var pref = ActorOf(Props.Create(() => new UserStashFailureActor(Name)));
            pref.Tell(new Cmd("a"));
            for (int i = 1; i <= 10; i++)
            {
                var cmd = new Cmd("b-" + i);
                pref.Tell(cmd);
            }
            pref.Tell(new Cmd("c"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a", "c", "b-1", "b-3", "b-4", "b-5", "b-6", "b-7", "b-8", "b-9", "b-10");
        }

        [Fact]
        public void PersistentActor_should_support_user_stash_operations_within_handler_under_failures()
        {
            var pref = ActorOf(Props.Create(() => new UserStashWithinHandlerFailureCallbackActor(Name)));
            pref.Tell(new Cmd("a"));
            for (int i = 1; i <= 10; i++)
            {
                var cmd = new Cmd("b-" + i);
                pref.Tell(cmd);
            }
            pref.Tell(new Cmd("c"));
            pref.Tell(GetState.Instance);
            ExpectMsgInOrder("a", "c", "b-1", "b-3", "b-4", "b-5", "b-6", "b-7", "b-8", "b-9", "b-10");
        }
    }

    public class SteppingMemoryPersistentActorStashingSpec : PersistenceSpec
    {
        internal class AsyncStashingActor : PersistentActorStashingSpec.StashExamplePersistentActor
        {
            private bool _stashed = false;

            public AsyncStashingActor(string name) : base(name)
            {
            }

            protected override bool ReceiveCommand(object message)
            {
                if (!CommonBehavior(message) && !UnstashBehavior(message))
                {
                    if (message is Cmd)
                    {
                        var data = ((Cmd) message).Data;
                        if (data.Equals("a"))
                        {
                            PersistAsync(new Evt("a"), UpdateStateHandler);
                            return true;
                        }
                        if (data.Equals("b"))
                        {
                            if (!_stashed)
                            {
                                Stash.Stash();
                                _stashed = true;
                            }
                            else
                                PersistAsync(new Evt("b"), UpdateStateHandler);
                            return true;
                        }
                    }
                }
                else return true;
                return false;
            }

            protected override bool UnstashBehavior(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "c")
                {
                    PersistAsync(new Evt("c"), UpdateStateHandler);
                    Stash.UnstashAll();
                }
                else return false;
                return true;
            }
        }

        internal class AsyncStashingWithinHandlerActor : AsyncStashingActor
        {
            public AsyncStashingWithinHandlerActor(string name) : base(name)
            {
            }

            protected override bool UnstashBehavior(object message)
            {
                var cmd = message as Cmd;
                if (cmd != null && cmd.Data.ToString() == "c")
                {
                    PersistAsync(new Evt("c"), evt =>
                    {
                        UpdateState(evt);
                        Stash.UnstashAll();
                    });
                }
                else return false;
                return true;
            }
        }

        public SteppingMemoryPersistentActorStashingSpec()
            : base(SteppingMemoryJournal.Config("persistence-stash").WithFallback(Configuration("SteppingMemoryPersistentActorStashingSpec")))
        {
        }

        [Fact]
        public void Stashing_in_a_PersistentActor_mixed_with_PersistAsync_should_handle_async_callback_not_happening_until_next_message_has_been_stashed()
        {
            var pref = Sys.ActorOf(Props.Create(() => new AsyncStashingActor(Name)));
            AwaitAssert(() => SteppingMemoryJournal.GetRef("persistence-stash"), TimeSpan.FromSeconds(3));
            var journal = SteppingMemoryJournal.GetRef("persistence-stash");

            // initial read highest
            SteppingMemoryJournal.Step(journal);

            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));

            // allow the write to complete, after the stash
            SteppingMemoryJournal.Step(journal);

            pref.Tell(new Cmd("c"));
            // writing of c and b
            SteppingMemoryJournal.Step(journal);
            SteppingMemoryJournal.Step(journal);

            Within(TimeSpan.FromSeconds(3), () =>
            {
                AwaitAssert(() =>
                {
                    pref.Tell(GetState.Instance);
                    ExpectMsgInOrder("a", "c", "b");
                });
            });
        }

        [Fact]
        public void Stashing_in_a_PersistentActor_mixed_with_PersistAsync_should_handle_async_callback_not_happening_until_next_message_has_been_stashed_within_handler()
        {
            var pref = Sys.ActorOf(Props.Create(() => new AsyncStashingWithinHandlerActor(Name)));
            AwaitAssert(() => SteppingMemoryJournal.GetRef("persistence-stash"), TimeSpan.FromSeconds(3));
            var journal = SteppingMemoryJournal.GetRef("persistence-stash");

            // initial read highest
            SteppingMemoryJournal.Step(journal);

            pref.Tell(new Cmd("a"));
            pref.Tell(new Cmd("b"));

            // allow the write to complete, after the stash
            SteppingMemoryJournal.Step(journal);

            pref.Tell(new Cmd("c"));
            // writing of c and b
            SteppingMemoryJournal.Step(journal);
            SteppingMemoryJournal.Step(journal);

            Within(TimeSpan.FromSeconds(3), () =>
            {
                AwaitAssert(() =>
                {
                    pref.Tell(GetState.Instance);
                    ExpectMsgInOrder("a", "c", "b");
                });
            });
        }
    }
}
