//-----------------------------------------------------------------------
// <copyright file="ActorTaskSchedulerSyncCompletionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Dispatch
{
    /// <summary>
    /// Pins the behaviour of <see cref="ActorTaskScheduler.RunTask(Func{Task})"/> and <c>ReceiveAsync</c>
    /// when the handler's task has already completed by the time the handler returns, and checks that faulted,
    /// cancelled and incomplete tasks and subclassed schedulers keep the continuation-based path.
    /// </summary>
    public class ActorTaskSchedulerSyncCompletionSpec : AkkaSpec
    {
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString(@"
            counting-dispatcher {
                type = """ + typeof(CountingDispatcherConfigurator).AssemblyQualifiedName + @"""
            }");

        public ActorTaskSchedulerSyncCompletionSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        #region Handler context

        private sealed record HandlerContext(
            IActorRef Sender,
            IActorRef Self,
            IActorRef ContextSelf,
            bool CurrentCellIsOwnCell,
            bool RunsOnOwnTaskScheduler);

        private sealed class ContextActor : ReceiveActor
        {
            public ContextActor()
            {
                ReceiveAsync<string>(_ =>
                {
                    var cell = (ActorCell)Context;
                    Sender.Tell(new HandlerContext(
                        Sender,
                        Self,
                        Context.Self,
                        ActorCell.Current == cell,
                        TaskScheduler.Current == cell.TaskScheduler));
                    return Task.CompletedTask;
                });
            }
        }

        [Fact(DisplayName = "A completed ReceiveAsync handler runs with Sender, Self, Context and the actor's task scheduler")]
        public async Task Completed_handler_runs_in_actor_context()
        {
            var actor = Sys.ActorOf(Props.Create(() => new ContextActor()));

            actor.Tell("hello");

            var context = await ExpectMsgAsync<HandlerContext>();
            Assert.Equal(TestActor, context.Sender);
            Assert.Equal(actor, context.Self);
            Assert.Equal(actor, context.ContextSelf);
            Assert.True(context.CurrentCellIsOwnCell);
            // awaits inside the handler capture TaskScheduler.Current, so it must be the actor's scheduler
            Assert.True(context.RunsOnOwnTaskScheduler);
        }

        private sealed record AfterRunTask(
            object CurrentMessage,
            IActorRef Sender,
            bool CurrentCellIsOwnCell,
            bool MailboxSuspended,
            object SchedulerCurrentMessage,
            bool HandlerRan);

        private sealed class AfterRunTaskActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                var handlerRan = false;
                RunTask(() =>
                {
                    handlerRan = true;
                    return Task.CompletedTask;
                });

                var cell = (ActorCell)Context;
                Sender.Tell(new AfterRunTask(
                    cell.CurrentMessage,
                    Sender,
                    ActorCell.Current == cell,
                    cell.Mailbox.IsSuspended(),
                    cell.TaskScheduler.CurrentMessage,
                    handlerRan));
            }
        }

        [Fact(DisplayName = "After a completed RunTask the receive continues with the cell current and the mailbox resumed")]
        public async Task Completed_RunTask_leaves_cell_state_as_the_mailbox_expects()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AfterRunTaskActor()));

            for (var i = 0; i < 3; i++)
            {
                actor.Tell(i);

                var after = await ExpectMsgAsync<AfterRunTask>();
                Assert.True(after.HandlerRan);
                Assert.Equal(i, after.CurrentMessage);
                Assert.Equal(TestActor, after.Sender);
                Assert.True(after.CurrentCellIsOwnCell);
                Assert.False(after.MailboxSuspended);
                Assert.Null(after.SchedulerCurrentMessage);
            }
        }

        #endregion

        #region Ordering and re-entrancy

        private sealed class SelfTellActor : UntypedActor
        {
            private readonly List<string> _log = new();
            private int _inReceive;

            protected override void OnReceive(object message)
            {
                if (Interlocked.Exchange(ref _inReceive, 1) != 0)
                    _log.Add("re-entered");

                switch (message)
                {
                    case "first":
                        RunTask(() =>
                        {
                            _log.Add("first:handler");
                            Self.Tell("second");
                            return Task.CompletedTask;
                        });
                        _log.Add("first:after-run-task");
                        break;
                    case "second":
                        _log.Add("second");
                        break;
                    case "log":
                        Sender.Tell(_log.ToArray());
                        break;
                }

                Volatile.Write(ref _inReceive, 0);
            }
        }

        [Fact(DisplayName = "A message a completed handler sends to Self is processed after the current message")]
        public async Task Self_tell_is_processed_after_current_message()
        {
            var actor = Sys.ActorOf(Props.Create(() => new SelfTellActor()));

            actor.Tell("first");
            await AwaitAssertAsync(async () =>
            {
                var log = await actor.Ask<string[]>("log", RemainingOrDefault);
                Assert.Equal(new[] { "first:handler", "first:after-run-task", "second" }, log);
            });
        }

        private sealed class OrderingActor : ReceiveActor
        {
            private readonly List<int> _seen = new();

            public OrderingActor()
            {
                ReceiveAsync<int>(n => n % 10 == 0, async n =>
                {
                    // Task.Yield would resume inline on the actor's task scheduler; a delay really completes later
                    await Task.Delay(1);
                    _seen.Add(n);
                });
                ReceiveAsync<int>(n => n % 2 == 0, n =>
                {
                    _seen.Add(n);
                    return Task.CompletedTask;
                });
                Receive<int>(n => _seen.Add(n));
                Receive<string>(_ => Sender.Tell(_seen.ToArray()));
            }
        }

        [Fact(DisplayName = "Completed, incomplete and synchronous handlers keep mailbox order")]
        public async Task Mixed_handlers_keep_mailbox_order()
        {
            var actor = Sys.ActorOf(Props.Create(() => new OrderingActor()));

            for (var i = 1; i <= 500; i++)
                actor.Tell(i);

            var seen = await actor.Ask<int[]>("seen", RemainingOrDefault);
            Assert.Equal(Enumerable.Range(1, 500), seen);
        }

        #endregion

        #region Become and Stash

        private sealed class BecomeActor : ReceiveActor
        {
            public BecomeActor()
            {
                Initial();
            }

            private void Initial()
            {
                ReceiveAsync<string>(s => s == "become", _ =>
                {
                    BecomeStacked(Other);
                    Sender.Tell("became");
                    return Task.CompletedTask;
                });
                ReceiveAsync<string>(s => s == "replace", _ =>
                {
                    Become(Initial);
                    Sender.Tell("replaced");
                    return Task.CompletedTask;
                });
                Receive<string>(s => Sender.Tell("initial:" + s));
            }

            private void Other()
            {
                ReceiveAsync<string>(s => s == "unbecome", _ =>
                {
                    UnbecomeStacked();
                    Sender.Tell("unbecame");
                    return Task.CompletedTask;
                });
                Receive<string>(s => Sender.Tell("other:" + s));
            }
        }

        [Fact(DisplayName = "Become and Unbecome inside a completed handler apply to the next message")]
        public async Task Become_inside_completed_handler_applies_to_next_message()
        {
            var actor = Sys.ActorOf(Props.Create(() => new BecomeActor()));

            foreach (var m in new[] { "a", "become", "b", "unbecome", "c", "replace", "d" })
                actor.Tell(m);

            foreach (var expected in new[] { "initial:a", "became", "other:b", "unbecame", "initial:c", "replaced", "initial:d" })
                await ExpectMsgAsync(expected);
        }

        private sealed record Stashed(int N, IActorRef Sender);

        private sealed class StashingActor : ReceiveActor, IWithStash
        {
            public IStash Stash { get; set; } = null!;

            public StashingActor()
            {
                ReceiveAsync<int>(_ =>
                {
                    Stash.Stash();
                    return Task.CompletedTask;
                });
                ReceiveAsync<string>(s => s == "open", _ =>
                {
                    Become(Open);
                    Stash.UnstashAll();
                    return Task.CompletedTask;
                });
            }

            private void Open()
            {
                ReceiveAsync<int>(n =>
                {
                    Sender.Tell(new Stashed(n, Sender));
                    return Task.CompletedTask;
                });
            }
        }

        [Fact(DisplayName = "Stash and UnstashAll inside completed handlers preserve order and Sender")]
        public async Task Stash_inside_completed_handler_preserves_order_and_sender()
        {
            var actor = Sys.ActorOf(Props.Create(() => new StashingActor()));
            var other = CreateTestProbe();

            actor.Tell(1);
            actor.Tell(2, other.Ref);
            actor.Tell(3);
            actor.Tell("open");
            actor.Tell(4);

            Assert.Equal(1, (await ExpectMsgAsync<Stashed>()).N);
            var second = await other.ExpectMsgAsync<Stashed>();
            Assert.Equal(2, second.N);
            Assert.Equal(other.Ref, second.Sender);
            Assert.Equal(3, (await ExpectMsgAsync<Stashed>()).N);
            var fourth = await ExpectMsgAsync<Stashed>();
            Assert.Equal(4, fourth.N);
            Assert.Equal(TestActor, fourth.Sender);
        }

        #endregion

        #region Failures keep the old path

        private sealed record Failed(Type ExceptionType, string Message);
        private sealed record Restarted(object FailedMessage);

        private sealed class FailingChild : ReceiveActor
        {
            private readonly IActorRef _probe;

            public FailingChild(IActorRef probe)
            {
                _probe = probe;
                ReceiveAsync<string>(s => s == "throw", _ => throw new InvalidOperationException("thrown"));
                ReceiveAsync<string>(s => s == "faulted", _ => Task.FromException(new InvalidOperationException("faulted")));
                ReceiveAsync<string>(s => s == "cancelled", _ => Task.FromCanceled(new CancellationToken(true)));
                ReceiveAsync<string>(s => s == "null", _ => null!);
                ReceiveAsync<string>(s =>
                {
                    _probe.Tell("handled:" + s);
                    return Task.CompletedTask;
                });
            }

            protected override void PreRestart(Exception reason, object message)
            {
                _probe.Tell(new Restarted(message));
                base.PreRestart(reason, message);
            }
        }

        private sealed class FailureParent : ReceiveActor
        {
            public FailureParent(IActorRef probe)
            {
                _probe = probe;
                var child = Context.ActorOf(Props.Create(() => new FailingChild(probe)), "child");
                ReceiveAny(m => child.Forward(m));
            }

            private readonly IActorRef _probe;

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(ex =>
                {
                    _probe.Tell(new Failed(ex.GetType(), ex.Message));
                    return Directive.Restart;
                });
            }
        }

        [Theory(DisplayName = "A handler that throws or returns a faulted, cancelled or null task is supervised as before")]
        [InlineData("throw", typeof(InvalidOperationException), "thrown")]
        [InlineData("faulted", typeof(InvalidOperationException), "faulted")]
        [InlineData("cancelled", typeof(TaskCanceledException), null)]
        [InlineData("null", typeof(TaskCanceledException), null)]
        public async Task Failed_handler_is_supervised(string command, Type exceptionType, string? exceptionMessage)
        {
            var probe = CreateTestProbe();
            var parent = Sys.ActorOf(Props.Create(() => new FailureParent(probe.Ref)));

            parent.Tell(command);
            parent.Tell("after");

            var failed = await probe.ExpectMsgAsync<Failed>();
            Assert.Equal(exceptionType, failed.ExceptionType);
            if (exceptionMessage != null)
                Assert.Equal(exceptionMessage, failed.Message);
            Assert.Equal(command, (await probe.ExpectMsgAsync<Restarted>()).FailedMessage);
            await probe.ExpectMsgAsync("handled:after");
        }

        #endregion

        #region Subclassed scheduler hooks

        private static readonly AsyncLocal<string?> Ambient = new();

        private interface IHookLog
        {
            ConcurrentQueue<string> Log { get; }
        }

        private sealed class HookedTaskScheduler : ActorTaskScheduler
        {
            private readonly ActorCell _cell;

            public HookedTaskScheduler(ActorCell cell) : base(cell)
            {
                _cell = cell;
            }

            private ConcurrentQueue<string> Log => ((IHookLog)_cell.Actor).Log;

            protected override void OnBeforeTaskStarted()
            {
                Log.Enqueue("before-hook");
            }

            protected override void OnAfterTaskCompleted()
            {
                Log.Enqueue($"after-hook:{Activity.Current?.OperationName}:{Ambient.Value}");
                // what a tracing integration does once the handler's span is finished
                Activity.Current = null;
                Ambient.Value = null;
            }
        }

        private sealed class HookedActorCell : ActorCell
        {
            private HookedTaskScheduler? _scheduler;

            public HookedActorCell(ActorSystemImpl system, IInternalActorRef self, Props props,
                MessageDispatcher dispatcher, IInternalActorRef parent)
                : base(system, self, props, dispatcher, parent)
            {
            }

            public override ActorTaskScheduler TaskScheduler
            {
                get
                {
                    var scheduler = Volatile.Read(ref _scheduler);
                    if (scheduler != null)
                        return scheduler;
                    scheduler = new HookedTaskScheduler(this);
                    return Interlocked.CompareExchange(ref _scheduler, scheduler, null) ?? scheduler;
                }
            }
        }

        /// <summary>
        /// A top-level actor whose cell uses <see cref="HookedTaskScheduler"/>, created the way TestKit creates TestActorRef.
        /// </summary>
        private sealed class HookedActorRef : LocalActorRef
        {
            private HookedActorRef(ActorSystemImpl system, Props props, MessageDispatcher dispatcher,
                MailboxType mailboxType, IInternalActorRef supervisor, ActorPath path)
                : base(system, props, dispatcher, mailboxType, supervisor, path)
            {
            }

            protected override ActorCell NewActorCell(ActorSystemImpl system, IInternalActorRef self, Props props,
                MessageDispatcher dispatcher, IInternalActorRef supervisor)
            {
                return new HookedActorCell(system, self, props, dispatcher, supervisor);
            }

            public static IActorRef Create(ActorSystem system, Props props, string name)
            {
                var systemImpl = (ActorSystemImpl)system;
                var supervisor = (LocalActorRef)systemImpl.Guardian;
                var dispatcher = system.Dispatchers.Lookup(Dispatchers.DefaultDispatcherId);
                supervisor.Cell.ReserveChild(name);
                var mailboxType = system.Mailboxes.GetMailboxType(props, dispatcher.Configurator.Config);
                var actorRef = new HookedActorRef(systemImpl, props, dispatcher, mailboxType, supervisor, supervisor.Path / name);
                actorRef.Start();
                return actorRef;
            }
        }

        private sealed class HookedActor : UntypedActor, IHookLog
        {
            private readonly ConcurrentQueue<string> _log = new();

            public ConcurrentQueue<string> Log => _log;

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case "log":
                        Sender.Tell(_log.ToArray());
                        return;
                    case "delay":
                        RunTask(async () =>
                        {
                            _log.Enqueue("handler:delay-start");
                            await Task.Delay(20);
                            _log.Enqueue("handler:delay-end");
                        });
                        _log.Enqueue("after-run-task:delay");
                        return;
                    case string id:
                        _log.Enqueue($"start:{Activity.Current?.OperationName}:{Ambient.Value}");
                        var previousAmbient = Ambient.Value;
                        using (var activity = new Activity("span-" + id).Start())
                        {
                            Ambient.Value = id;
                            RunTask(() =>
                            {
                                _log.Enqueue($"handler:{Activity.Current?.OperationName}:{Ambient.Value}");
                                return Task.CompletedTask;
                            });
                            // code that runs after the handler in the same receive (e.g. a journal write)
                            _log.Enqueue($"after-run-task:{Activity.Current?.OperationName}:{Ambient.Value}");
                        }
                        Ambient.Value = previousAmbient;
                        return;
                }
            }
        }

        [Fact(DisplayName = "A subclassed scheduler's hooks run once each, in order, and their AsyncLocal changes do not leak")]
        public async Task Subclassed_scheduler_hooks_are_called_once_and_do_not_leak()
        {
            var actor = HookedActorRef.Create(Sys, Props.Create(() => new HookedActor()), "hooked");

            actor.Tell("1");
            actor.Tell("2");
            actor.Tell("delay");
            var entries = await actor.Ask<string[]>("log", RemainingOrDefault);

            Assert.Equal(new[]
            {
                "start::",
                "before-hook",
                "handler:span-1:1",
                "after-hook:span-1:1",
                "after-run-task:span-1:1",
                "start::",
                "before-hook",
                "handler:span-2:2",
                "after-hook:span-2:2",
                "after-run-task:span-2:2",
                "before-hook",
                "handler:delay-start",
                "after-run-task:delay",
                "handler:delay-end",
                "after-hook::",
            }, entries);

            Sys.Stop(actor);
        }

        #endregion

        #region Mailbox schedules

        private sealed class AsyncEcho : ReceiveActor
        {
            public AsyncEcho()
            {
                ReceiveAsync<int>(n =>
                {
                    Sender.Tell(n);
                    return Task.CompletedTask;
                });
            }
        }

        private sealed class SyncEcho : ReceiveActor
        {
            public SyncEcho()
            {
                Receive<int>(n => Sender.Tell(n));
            }
        }

        [Fact(DisplayName = "A completed ReceiveAsync handler schedules the mailbox no more often than Receive")]
        public async Task Completed_handler_does_not_add_mailbox_schedules()
        {
            const int requests = 200;
            var dispatcher = (CountingDispatcher)Sys.Dispatchers.Lookup("counting-dispatcher");
            var asyncEcho = Sys.ActorOf(Props.Create(() => new AsyncEcho()).WithDispatcher("counting-dispatcher"));
            var syncEcho = Sys.ActorOf(Props.Create(() => new SyncEcho()).WithDispatcher("counting-dispatcher"));

            // let both actors finish starting before counting
            await asyncEcho.Ask<int>(-1, RemainingOrDefault);
            await syncEcho.Ask<int>(-1, RemainingOrDefault);

            var asyncBefore = dispatcher.SchedulesOf(asyncEcho);
            for (var i = 0; i < requests; i++)
                await asyncEcho.Ask<int>(i, RemainingOrDefault);
            var asyncSchedules = dispatcher.SchedulesOf(asyncEcho) - asyncBefore;

            var syncBefore = dispatcher.SchedulesOf(syncEcho);
            for (var i = 0; i < requests; i++)
                await syncEcho.Ask<int>(i, RemainingOrDefault);
            var syncSchedules = dispatcher.SchedulesOf(syncEcho) - syncBefore;

            Output.WriteLine($"mailbox schedules per request: ReceiveAsync {asyncSchedules / (double)requests:F2}, Receive {syncSchedules / (double)requests:F2}");
            // A request either finds the mailbox idle (one schedule) or arrives while the previous run is still
            // finishing (none); a handler whose task is already complete must not need a second schedule to resume.
            Assert.InRange(asyncSchedules, 1, requests + requests / 10);
            Assert.InRange(syncSchedules, 1, requests + requests / 10);
        }

        public sealed class CountingDispatcher : Dispatcher
        {
            private readonly ConcurrentDictionary<IActorRef, StrongBox> _schedules = new();

            public CountingDispatcher(MessageDispatcherConfigurator configurator, string id, int throughput,
                long? throughputDeadlineTime, ExecutorServiceFactory executorServiceFactory, TimeSpan shutdownTimeout)
                : base(configurator, id, throughput, throughputDeadlineTime, executorServiceFactory, shutdownTimeout)
            {
            }

            public long SchedulesOf(IActorRef actor) => Interlocked.Read(ref _schedules.GetOrAdd(actor, _ => new StrongBox()).Value);

            protected override void ExecuteTask(IRunnable run)
            {
                if (run is Mailbox mailbox && mailbox.Actor is { } cell)
                    Interlocked.Increment(ref _schedules.GetOrAdd(cell.Self, _ => new StrongBox()).Value);
                base.ExecuteTask(run);
            }

            private sealed class StrongBox
            {
                public long Value;
            }
        }

        public sealed class CountingDispatcherConfigurator : MessageDispatcherConfigurator
        {
            private readonly CountingDispatcher _instance;

            public CountingDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
            {
                _instance = new CountingDispatcher(this,
                    config.GetString("id", null),
                    config.GetInt("throughput", 0),
                    config.GetTimeSpan("throughput-deadline-time", null).Ticks,
                    ConfigureExecutor(),
                    config.GetTimeSpan("shutdown-timeout", null));
            }

            public override MessageDispatcher Dispatcher() => _instance;
        }

        #endregion
    }
}
