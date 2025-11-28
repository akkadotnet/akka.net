//-----------------------------------------------------------------------
// <copyright file="PersistenceCompletionCallbackSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    /// <summary>
    /// Tests for persistence completion callbacks and async handler support.
    /// </summary>
    public class PersistenceCompletionCallbackSpec : PersistenceSpec
    {
        public PersistenceCompletionCallbackSpec(ITestOutputHelper output)
            : base(Configuration("PersistenceCompletionCallbackSpec"), output)
        {
        }

        #region Test Actors

        private class TestEvent
        {
            public string Data { get; }
            public TestEvent(string data) => Data = data;
        }

        private class GetEvents
        {
            public static readonly GetEvents Instance = new();
            private GetEvents() { }
        }

        private class GetCompletionOrder
        {
            public static readonly GetCompletionOrder Instance = new();
            private GetCompletionOrder() { }
        }

        /// <summary>
        /// Actor that tests PersistAll with sync completion callback
        /// </summary>
        private class PersistAllWithCompletionActor : UntypedPersistentActor
        {
            private readonly List<string> _events = new();
            private readonly List<string> _completionOrder = new();
            private readonly IActorRef _probe;

            public override string PersistenceId { get; }

            public PersistAllWithCompletionActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message)
            {
                if (message is TestEvent evt)
                    _events.Add(evt.Data);
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case string[] events:
                        var testEvents = new List<TestEvent>();
                        foreach (var e in events)
                            testEvents.Add(new TestEvent(e));

                        PersistAll(testEvents, evt =>
                        {
                            _events.Add(evt.Data);
                            _completionOrder.Add($"handler:{evt.Data}");
                        }, () =>
                        {
                            _completionOrder.Add("completion");
                            _probe.Tell("completed");
                        });
                        break;

                    case GetEvents:
                        Sender.Tell(_events.ToArray());
                        break;

                    case GetCompletionOrder:
                        Sender.Tell(_completionOrder.ToArray());
                        break;
                }
            }
        }

        /// <summary>
        /// Actor that tests PersistAll with async completion callback
        /// </summary>
        private class PersistAllWithAsyncCompletionActor : UntypedPersistentActor
        {
            private readonly List<string> _events = new();
            private readonly List<string> _completionOrder = new();
            private readonly IActorRef _probe;

            public override string PersistenceId { get; }

            public PersistAllWithAsyncCompletionActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message)
            {
                if (message is TestEvent evt)
                    _events.Add(evt.Data);
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case string[] events:
                        var testEvents = new List<TestEvent>();
                        foreach (var e in events)
                            testEvents.Add(new TestEvent(e));

                        PersistAll(testEvents, evt =>
                        {
                            _events.Add(evt.Data);
                            _completionOrder.Add($"handler:{evt.Data}");
                        }, async () =>
                        {
                            await Task.Delay(10);
                            _completionOrder.Add("async-completion");
                            _probe.Tell("completed");
                        });
                        break;

                    case GetEvents:
                        Sender.Tell(_events.ToArray());
                        break;

                    case GetCompletionOrder:
                        Sender.Tell(_completionOrder.ToArray());
                        break;
                }
            }
        }

        /// <summary>
        /// Actor that tests PersistAllAsync with sync completion callback
        /// </summary>
        private class PersistAllAsyncWithCompletionActor : UntypedPersistentActor
        {
            private readonly List<string> _events = new();
            private readonly List<string> _completionOrder = new();
            private readonly IActorRef _probe;

            public override string PersistenceId { get; }

            public PersistAllAsyncWithCompletionActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message)
            {
                if (message is TestEvent evt)
                    _events.Add(evt.Data);
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case string[] events:
                        var testEvents = new List<TestEvent>();
                        foreach (var e in events)
                            testEvents.Add(new TestEvent(e));

                        PersistAllAsync(testEvents, evt =>
                        {
                            _events.Add(evt.Data);
                            _completionOrder.Add($"handler:{evt.Data}");
                        }, () =>
                        {
                            _completionOrder.Add("completion");
                            _probe.Tell("completed");
                        });
                        break;

                    case GetEvents:
                        Sender.Tell(_events.ToArray());
                        break;

                    case GetCompletionOrder:
                        Sender.Tell(_completionOrder.ToArray());
                        break;
                }
            }
        }

        /// <summary>
        /// Actor that tests Persist with async handler
        /// </summary>
        private class PersistWithAsyncHandlerActor : UntypedPersistentActor
        {
            private readonly List<string> _events = new();
            private readonly List<string> _completionOrder = new();
            private readonly IActorRef _probe;

            public override string PersistenceId { get; }

            public PersistWithAsyncHandlerActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message)
            {
                if (message is TestEvent evt)
                    _events.Add(evt.Data);
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case string eventData:
                        Persist(new TestEvent(eventData), async evt =>
                        {
                            await Task.Delay(10);
                            _events.Add(evt.Data);
                            _completionOrder.Add($"async-handler:{evt.Data}");
                            _probe.Tell("handled");
                        });
                        break;

                    case GetEvents:
                        Sender.Tell(_events.ToArray());
                        break;

                    case GetCompletionOrder:
                        Sender.Tell(_completionOrder.ToArray());
                        break;
                }
            }
        }

        /// <summary>
        /// Actor that tests PersistAsync with async handler
        /// </summary>
        private class PersistAsyncWithAsyncHandlerActor : UntypedPersistentActor
        {
            private readonly List<string> _events = new();
            private readonly List<string> _completionOrder = new();
            private readonly IActorRef _probe;

            public override string PersistenceId { get; }

            public PersistAsyncWithAsyncHandlerActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message)
            {
                if (message is TestEvent evt)
                    _events.Add(evt.Data);
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case string eventData:
                        PersistAsync(new TestEvent(eventData), async evt =>
                        {
                            await Task.Delay(10);
                            _events.Add(evt.Data);
                            _completionOrder.Add($"async-handler:{evt.Data}");
                            _probe.Tell("handled");
                        });
                        break;

                    case GetEvents:
                        Sender.Tell(_events.ToArray());
                        break;

                    case GetCompletionOrder:
                        Sender.Tell(_completionOrder.ToArray());
                        break;
                }
            }
        }

        /// <summary>
        /// Actor that tests DeferAsync with async handler
        /// </summary>
        private class DeferAsyncWithAsyncHandlerActor : UntypedPersistentActor
        {
            private readonly List<string> _events = new();
            private readonly List<string> _completionOrder = new();
            private readonly IActorRef _probe;

            public override string PersistenceId { get; }

            public DeferAsyncWithAsyncHandlerActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message)
            {
                if (message is TestEvent evt)
                    _events.Add(evt.Data);
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case string[] events:
                        // First persist events, then defer async
                        var testEvents = new List<TestEvent>();
                        foreach (var e in events)
                            testEvents.Add(new TestEvent(e));

                        PersistAllAsync(testEvents, evt =>
                        {
                            _events.Add(evt.Data);
                            _completionOrder.Add($"handler:{evt.Data}");
                        });

                        DeferAsync("deferred", async _ =>
                        {
                            await Task.Delay(10);
                            _completionOrder.Add("async-deferred");
                            _probe.Tell("deferred");
                        });
                        break;

                    case GetEvents:
                        Sender.Tell(_events.ToArray());
                        break;

                    case GetCompletionOrder:
                        Sender.Tell(_completionOrder.ToArray());
                        break;
                }
            }
        }

        /// <summary>
        /// Actor that tests PersistAll with async handlers
        /// </summary>
        private class PersistAllWithAsyncHandlerActor : UntypedPersistentActor
        {
            private readonly List<string> _events = new();
            private readonly List<string> _completionOrder = new();
            private readonly IActorRef _probe;

            public override string PersistenceId { get; }

            public PersistAllWithAsyncHandlerActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message)
            {
                if (message is TestEvent evt)
                    _events.Add(evt.Data);
            }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case string[] events:
                        var testEvents = new List<TestEvent>();
                        foreach (var e in events)
                            testEvents.Add(new TestEvent(e));

                        PersistAll(testEvents, async evt =>
                        {
                            await Task.Delay(10);
                            _events.Add(evt.Data);
                            _completionOrder.Add($"async-handler:{evt.Data}");
                        }, () =>
                        {
                            _completionOrder.Add("completion");
                            _probe.Tell("completed");
                        });
                        break;

                    case GetEvents:
                        Sender.Tell(_events.ToArray());
                        break;

                    case GetCompletionOrder:
                        Sender.Tell(_completionOrder.ToArray());
                        break;
                }
            }
        }

        /// <summary>
        /// Actor that tests stashing behavior - commands should be stashed during PersistAll
        /// </summary>
        private class StashingBehaviorTestActor : UntypedPersistentActor
        {
            private readonly List<string> _commandOrder = new();
            private readonly IActorRef _probe;

            public override string PersistenceId { get; }

            public StashingBehaviorTestActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message) { }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case "persist":
                        _commandOrder.Add("persist-start");
                        PersistAll(new[] { new TestEvent("a"), new TestEvent("b") }, evt =>
                        {
                            _commandOrder.Add($"handler:{evt.Data}");
                        }, () =>
                        {
                            _commandOrder.Add("completion");
                        });
                        _commandOrder.Add("persist-end");
                        break;

                    case "other":
                        _commandOrder.Add("other-command");
                        _probe.Tell("other-processed");
                        break;

                    case "get-order":
                        Sender.Tell(_commandOrder.ToArray());
                        break;
                }
            }
        }

        /// <summary>
        /// Actor that tests empty event list with completion callback
        /// </summary>
        private class EmptyEventsWithCompletionActor : UntypedPersistentActor
        {
            private readonly IActorRef _probe;
            private bool _completionCalled;

            public override string PersistenceId { get; }

            public EmptyEventsWithCompletionActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;
            }

            protected override void OnRecover(object message) { }

            protected override void OnCommand(object message)
            {
                switch (message)
                {
                    case "persist-empty":
                        PersistAll(Array.Empty<TestEvent>(), _ => { }, () =>
                        {
                            _completionCalled = true;
                            _probe.Tell("completed");
                        });
                        break;

                    case "check":
                        Sender.Tell(_completionCalled);
                        break;
                }
            }
        }

        #endregion

        #region Tests

        [Fact(DisplayName = "PersistAll with sync completion callback should invoke callback after all handlers")]
        public void PersistAll_WithSyncCompletion_Should_InvokeAfterAllHandlers()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new PersistAllWithCompletionActor(Name, probe)));

            actor.Tell(new[] { "event1", "event2", "event3" });
            probe.ExpectMsg("completed");

            actor.Tell(GetCompletionOrder.Instance);
            var order = ExpectMsg<string[]>();

            order.Should().BeEquivalentTo(new[]
            {
                "handler:event1",
                "handler:event2",
                "handler:event3",
                "completion"
            }, options => options.WithStrictOrdering());
        }

        [Fact(DisplayName = "PersistAll with async completion callback should invoke callback after all handlers")]
        public void PersistAll_WithAsyncCompletion_Should_InvokeAfterAllHandlers()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new PersistAllWithAsyncCompletionActor(Name, probe)));

            actor.Tell(new[] { "event1", "event2", "event3" });
            probe.ExpectMsg("completed");

            actor.Tell(GetCompletionOrder.Instance);
            var order = ExpectMsg<string[]>();

            order.Should().BeEquivalentTo(new[]
            {
                "handler:event1",
                "handler:event2",
                "handler:event3",
                "async-completion"
            }, options => options.WithStrictOrdering());
        }

        [Fact(DisplayName = "PersistAllAsync with sync completion callback should invoke callback after all handlers")]
        public void PersistAllAsync_WithSyncCompletion_Should_InvokeAfterAllHandlers()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new PersistAllAsyncWithCompletionActor(Name, probe)));

            actor.Tell(new[] { "event1", "event2", "event3" });
            probe.ExpectMsg("completed");

            actor.Tell(GetCompletionOrder.Instance);
            var order = ExpectMsg<string[]>();

            order.Should().BeEquivalentTo(new[]
            {
                "handler:event1",
                "handler:event2",
                "handler:event3",
                "completion"
            }, options => options.WithStrictOrdering());
        }

        [Fact(DisplayName = "Persist with async handler should execute handler asynchronously")]
        public void Persist_WithAsyncHandler_Should_ExecuteAsynchronously()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new PersistWithAsyncHandlerActor(Name, probe)));

            actor.Tell("event1");
            probe.ExpectMsg("handled");

            actor.Tell(GetCompletionOrder.Instance);
            var order = ExpectMsg<string[]>();

            order.Should().Contain("async-handler:event1");
        }

        [Fact(DisplayName = "PersistAsync with async handler should execute handler asynchronously")]
        public void PersistAsync_WithAsyncHandler_Should_ExecuteAsynchronously()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new PersistAsyncWithAsyncHandlerActor(Name, probe)));

            actor.Tell("event1");
            probe.ExpectMsg("handled");

            actor.Tell(GetCompletionOrder.Instance);
            var order = ExpectMsg<string[]>();

            order.Should().Contain("async-handler:event1");
        }

        [Fact(DisplayName = "DeferAsync with async handler should execute after pending invocations")]
        public void DeferAsync_WithAsyncHandler_Should_ExecuteAfterPending()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new DeferAsyncWithAsyncHandlerActor(Name, probe)));

            actor.Tell(new[] { "event1", "event2" });
            probe.ExpectMsg("deferred");

            actor.Tell(GetCompletionOrder.Instance);
            var order = ExpectMsg<string[]>();

            order.Should().BeEquivalentTo(new[]
            {
                "handler:event1",
                "handler:event2",
                "async-deferred"
            }, options => options.WithStrictOrdering());
        }

        [Fact(DisplayName = "PersistAll with async handlers should execute handlers and completion in order")]
        public void PersistAll_WithAsyncHandlers_Should_ExecuteInOrder()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new PersistAllWithAsyncHandlerActor(Name, probe)));

            actor.Tell(new[] { "event1", "event2" });
            probe.ExpectMsg("completed");

            actor.Tell(GetCompletionOrder.Instance);
            var order = ExpectMsg<string[]>();

            order.Should().BeEquivalentTo(new[]
            {
                "async-handler:event1",
                "async-handler:event2",
                "completion"
            }, options => options.WithStrictOrdering());
        }

        [Fact(DisplayName = "PersistAll should stash commands until completion callback finishes")]
        public void PersistAll_Should_StashCommandsUntilCompletion()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new StashingBehaviorTestActor(Name, probe)));

            // Send persist command followed immediately by another command
            actor.Tell("persist");
            actor.Tell("other");

            // Wait for the other command to be processed (after completion)
            probe.ExpectMsg("other-processed");

            actor.Tell("get-order");
            var order = ExpectMsg<string[]>();

            // The "other" command should be processed after the completion callback
            order.Should().BeEquivalentTo(new[]
            {
                "persist-start",
                "persist-end",
                "handler:a",
                "handler:b",
                "completion",
                "other-command"
            }, options => options.WithStrictOrdering());
        }

        [Fact(DisplayName = "PersistAll with empty events should invoke completion callback immediately")]
        public void PersistAll_WithEmptyEvents_Should_InvokeCompletionImmediately()
        {
            var probe = CreateTestProbe();
            var actor = ActorOf(Props.Create(() =>
                new EmptyEventsWithCompletionActor(Name, probe)));

            actor.Tell("persist-empty");
            probe.ExpectMsg("completed");

            actor.Tell("check");
            var completionCalled = ExpectMsg<bool>();
            completionCalled.Should().BeTrue();
        }

        #endregion
    }
}
