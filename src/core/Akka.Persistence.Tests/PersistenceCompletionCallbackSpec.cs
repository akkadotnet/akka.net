//-----------------------------------------------------------------------
// <copyright file="PersistenceCompletionCallbackSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    public class PersistenceCompletionCallbackSpec : PersistenceSpec
    {
        public PersistenceCompletionCallbackSpec(ITestOutputHelper output)
            : base(Configuration("PersistenceCompletionCallbackSpec"), output)
        {
        }

        #region Test Actors

        internal class CompletionTestActor : ReceivePersistentActor
        {
            private readonly IActorRef _probe;
            private readonly List<string> _events = new();

            public CompletionTestActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;

                Command<string>(cmd =>
                {
                    if (cmd.StartsWith("persist-all-async-"))
                    {
                        var events = new[] { $"{cmd}-1", $"{cmd}-2", $"{cmd}-3" };
                        PersistAllAsync(events,
                            evt => _events.Add(evt),
                            onComplete: () => _probe.Tell($"completed-{cmd}"));
                    }
                    else if (cmd.StartsWith("persist-all-"))
                    {
                        var events = new[] { $"{cmd}-1", $"{cmd}-2", $"{cmd}-3" };
                        PersistAll(events,
                            evt => _events.Add(evt),
                            onComplete: () => _probe.Tell($"completed-{cmd}"));
                    }
                    else if (cmd == "get-state")
                    {
                        _probe.Tell(_events.ToList());
                    }
                });

                Recover<string>(evt => _events.Add(evt));
            }

            public override string PersistenceId { get; }
        }

        internal class AsyncCompletionTestActor : ReceivePersistentActor
        {
            private readonly IActorRef _probe;
            private readonly List<string> _events = new();

            public AsyncCompletionTestActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;

                Command<string>(cmd =>
                {
                    if (cmd.StartsWith("persist-all-async-"))
                    {
                        var events = new[] { $"{cmd}-1", $"{cmd}-2", $"{cmd}-3" };
                        PersistAllAsync(events,
                            evt => _events.Add(evt),
                            onCompleteAsync: async () =>
                            {
                                await Task.Delay(10); // Simulate async work
                                _probe.Tell($"async-completed-{cmd}");
                            });
                    }
                    else if (cmd.StartsWith("persist-all-"))
                    {
                        var events = new[] { $"{cmd}-1", $"{cmd}-2", $"{cmd}-3" };
                        PersistAll(events,
                            evt => _events.Add(evt),
                            onCompleteAsync: async () =>
                            {
                                await Task.Delay(10); // Simulate async work
                                _probe.Tell($"async-completed-{cmd}");
                            });
                    }
                    else if (cmd == "get-state")
                    {
                        _probe.Tell(_events.ToList());
                    }
                });

                Recover<string>(evt => _events.Add(evt));
            }

            public override string PersistenceId { get; }
        }

        internal class AsyncHandlerTestActor : ReceivePersistentActor
        {
            private readonly IActorRef _probe;
            private readonly List<string> _events = new();

            public AsyncHandlerTestActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;

                Command<string>(cmd =>
                {
                    if (cmd.StartsWith("persist-all-async-"))
                    {
                        var events = new[] { $"{cmd}-1", $"{cmd}-2", $"{cmd}-3" };
                        PersistAllAsync(events,
                            async evt =>
                            {
                                await Task.Delay(5); // Simulate async handler work
                                _events.Add(evt);
                            },
                            onComplete: () => _probe.Tell($"completed-{cmd}"));
                    }
                    else if (cmd.StartsWith("persist-async-"))
                    {
                        PersistAsync(cmd,
                            async evt =>
                            {
                                await Task.Delay(5); // Simulate async handler work
                                _events.Add(evt);
                                _probe.Tell($"persisted-{evt}");
                            });
                    }
                    else if (cmd == "get-state")
                    {
                        _probe.Tell(_events.ToList());
                    }
                });

                Recover<string>(evt => _events.Add(evt));
            }

            public override string PersistenceId { get; }
        }

        #endregion

        [Fact]
        public void PersistAllAsync_should_invoke_completion_callback_after_all_events_persisted()
        {
            var pid = "completion-1";
            var actor = Sys.ActorOf(Props.Create(() => new CompletionTestActor(pid, TestActor)));

            actor.Tell("persist-all-async-test");
            ExpectMsg("completed-persist-all-async-test", TimeSpan.FromSeconds(3));

            actor.Tell("get-state");
            var state = ExpectMsg<List<string>>();
            Assert.Equal(3, state.Count);
            Assert.Equal("persist-all-async-test-1", state[0]);
            Assert.Equal("persist-all-async-test-2", state[1]);
            Assert.Equal("persist-all-async-test-3", state[2]);
        }

        [Fact]
        public void PersistAll_should_invoke_completion_callback_after_all_events_persisted()
        {
            var pid = "completion-2";
            var actor = Sys.ActorOf(Props.Create(() => new CompletionTestActor(pid, TestActor)));

            actor.Tell("persist-all-test");
            ExpectMsg("completed-persist-all-test", TimeSpan.FromSeconds(3));

            actor.Tell("get-state");
            var state = ExpectMsg<List<string>>();
            Assert.Equal(3, state.Count);
            Assert.Equal("persist-all-test-1", state[0]);
            Assert.Equal("persist-all-test-2", state[1]);
            Assert.Equal("persist-all-test-3", state[2]);
        }

        [Fact]
        public void PersistAllAsync_should_invoke_async_completion_callback()
        {
            var pid = "async-completion-1";
            var actor = Sys.ActorOf(Props.Create(() => new AsyncCompletionTestActor(pid, TestActor)));

            actor.Tell("persist-all-async-test");
            ExpectMsg("async-completed-persist-all-async-test", TimeSpan.FromSeconds(3));

            actor.Tell("get-state");
            var state = ExpectMsg<List<string>>();
            Assert.Equal(3, state.Count);
        }

        [Fact]
        public void PersistAll_should_invoke_async_completion_callback()
        {
            var pid = "async-completion-2";
            var actor = Sys.ActorOf(Props.Create(() => new AsyncCompletionTestActor(pid, TestActor)));

            actor.Tell("persist-all-test");
            ExpectMsg("async-completed-persist-all-test", TimeSpan.FromSeconds(3));

            actor.Tell("get-state");
            var state = ExpectMsg<List<string>>();
            Assert.Equal(3, state.Count);
        }

        [Fact]
        public void PersistAllAsync_should_support_async_handlers()
        {
            var pid = "async-handler-1";
            var actor = Sys.ActorOf(Props.Create(() => new AsyncHandlerTestActor(pid, TestActor)));

            actor.Tell("persist-all-async-test");
            ExpectMsg("completed-persist-all-async-test", TimeSpan.FromSeconds(3));

            actor.Tell("get-state");
            var state = ExpectMsg<List<string>>();
            Assert.Equal(3, state.Count);
            Assert.Equal("persist-all-async-test-1", state[0]);
            Assert.Equal("persist-all-async-test-2", state[1]);
            Assert.Equal("persist-all-async-test-3", state[2]);
        }

        [Fact]
        public void PersistAsync_should_support_async_handlers()
        {
            var pid = "async-handler-2";
            var actor = Sys.ActorOf(Props.Create(() => new AsyncHandlerTestActor(pid, TestActor)));

            actor.Tell("persist-async-test");
            ExpectMsg("persisted-persist-async-test", TimeSpan.FromSeconds(3));

            actor.Tell("get-state");
            var state = ExpectMsg<List<string>>();
            Assert.Single(state);
            Assert.Equal("persist-async-test", state[0]);
        }

        [Fact]
        public void Completion_callback_should_fire_exactly_once_per_batch()
        {
            var pid = "completion-3";
            var actor = Sys.ActorOf(Props.Create(() => new CompletionTestActor(pid, TestActor)));

            // Send multiple commands
            actor.Tell("persist-all-async-batch1");
            actor.Tell("persist-all-async-batch2");
            actor.Tell("persist-all-async-batch3");

            // Should get exactly 3 completion messages, one per batch
            ExpectMsg("completed-persist-all-async-batch1", TimeSpan.FromSeconds(3));
            ExpectMsg("completed-persist-all-async-batch2", TimeSpan.FromSeconds(3));
            ExpectMsg("completed-persist-all-async-batch3", TimeSpan.FromSeconds(3));

            actor.Tell("get-state");
            var state = ExpectMsg<List<string>>();
            Assert.Equal(9, state.Count); // 3 batches * 3 events each
        }

        [Fact]
        public void Empty_event_list_should_invoke_completion_callback_immediately()
        {
            var pid = "completion-4";
            var probe = CreateTestProbe();
            var actor = Sys.ActorOf(Props.Create(() => new EmptyBatchTestActor(pid, probe.Ref)));

            actor.Tell("persist-empty");
            probe.ExpectMsg("completed-empty", TimeSpan.FromSeconds(3));
        }

        internal class EmptyBatchTestActor : ReceivePersistentActor
        {
            private readonly IActorRef _probe;

            public EmptyBatchTestActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;

                Command<string>(cmd =>
                {
                    if (cmd == "persist-empty")
                    {
                        var events = Array.Empty<string>();
                        PersistAllAsync(events,
                            evt => { },
                            onComplete: () => _probe.Tell("completed-empty"));
                    }
                });

                Recover<string>(evt => { });
            }

            public override string PersistenceId { get; }
        }
    }
}
