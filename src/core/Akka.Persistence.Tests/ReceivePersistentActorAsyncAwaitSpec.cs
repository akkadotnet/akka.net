//-----------------------------------------------------------------------
// <copyright file="ReceivePersistentActorAsyncAwaitSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    class ReceiveTimeoutAsyncActor : ReceivePersistentActor
    {
        private IActorRef _replyTo;

        public override string PersistenceId { get; }

        public ReceiveTimeoutAsyncActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            Command<ReceiveTimeout>(_ =>
            {
                _replyTo.Tell("GotIt");
            });
            CommandAsync<string>(async _ =>
            {
                _replyTo = Sender;

                await Task.Delay(TimeSpan.FromMilliseconds(100));
                SetReceiveTimeout(TimeSpan.FromMilliseconds(100));
            });
        }
    }
    class AsyncActor : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public AsyncActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            CommandAsync<string>(async s =>
            {
                await Task.Yield();
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                if (s == "stop")
                {
                    Sender.Tell("done");
                }
            });
        }
    }

    public class SuspendActor : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public SuspendActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            var state = 0;
            Command<string>(s => s == "change", _ =>
            {
                state = 1;
            });
            CommandAsync<string>(async m_ =>
            {
                Self.Tell("change");
                await Task.Delay(TimeSpan.FromSeconds(1));
                var cell = (ActorCell)Context;
                var current = cell.CurrentMessage;
                //ensure we have the correct current message here
                Assert.Same(m_, current);
                //we expect that state should not have changed due to an incoming message
                Sender.Tell(state);
            });
        }
    }

    public class AsyncAwaitActor : ReceivePersistentActor
    {
        /// <summary>
        /// Marker class for testing - sending this will get a synchronous "pong" reply.
        /// Use this to verify the actor has completed recovery before testing async handlers.
        /// </summary>
        public sealed class Ping
        {
            public static readonly Ping Instance = new();
            private Ping() { }
        }

        public override string PersistenceId { get; }

        public AsyncAwaitActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            // Synchronous handler for verifying actor is ready
            Command<Ping>(_ => Sender.Tell("pong"));

            CommandAsync<string>(async _ =>
            {
                var sender = Sender;
                var self = Self;
                await Task.Yield();
                await Task.Delay(TimeSpan.FromSeconds(1));
                Assert.Same(sender, Sender);
                Assert.Same(self, Self);
                Sender.Tell("done");
            });

            CommandAsync<int>(async _ =>
            {
                await Task.Yield();
                Sender.Tell("handled");
            }, i => i > 10);

            CommandAsync(typeof(double), async _ =>
            {
                await Task.Yield();
                Sender.Tell("handled");
            });

            CommandAnyAsync(async _ =>
            {
                await Task.Yield();
                Sender.Tell("receiveany");
            });
        }
    }

    public class PersistentAsyncAwaitActor : PersistentActor
    {
        public override string PersistenceId { get; }

        public PersistentAsyncAwaitActor(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        protected override bool ReceiveRecover(object message)
        {
            return false;
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message is string)
            {
                RunTask(async () =>
                {
                    var sender = Sender;
                    var self = Self;
                    await Task.Yield();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    Assert.Same(sender, Sender);
                    Assert.Same(self, Self);
                    Sender.Tell("done");
                });
                return true;
            }
            return false;
        }
    }

    public class Asker : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public Asker(string persistenceId, IActorRef other)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            CommandAsync<string>(async _ =>
            {
                var sender = Sender;
                var self = Self;
                var res = await other.Ask("start");
                Assert.Same(sender, Sender);
                Assert.Same(self, Self);
                Sender.Tell(res);
            });
        }
    }

    public class PersistentAsker : PersistentActor
    {
        private readonly IActorRef _other;

        public override string PersistenceId { get; }

        public PersistentAsker(string persistenceId, IActorRef other)
        {
            PersistenceId = persistenceId;
            _other = other;
        }

        protected override bool ReceiveRecover(object message)
        {
            return false;
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message is string)
            {
                RunTask(async () =>
                {
                    var sender = Sender;
                    var self = Self;
                    var res = await _other.Ask("start");
                    Assert.Same(sender, Sender);
                    Assert.Same(self, Self);
                    Sender.Tell(res);
                });
                return true;
            }
            return false;
        }
    }

    public class BlockingAsker : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public BlockingAsker(string persistenceId, IActorRef other)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            Command<string>(_ =>
            {
                //not async, blocking wait
                var res = other.Ask("start").Result;
                Sender.Tell(res);
            });
        }
    }

    public class BlockingAskSelf : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public BlockingAskSelf(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            Command<int>(_ =>
            {
                //since this actor is blocking in the handler below, it will never
                //be able to execute this section
                Sender.Tell("done");
            });
            Command<string>(_ =>
            {
                //ask and block
                var res = Self.Ask(123).Result;
                Sender.Tell(res);
            });
        }
    }

    public class AsyncExceptionActor : ReceivePersistentActor
    {
        private readonly IActorRef _callback;
        public override string PersistenceId { get; }

        public AsyncExceptionActor(string persistenceId, IActorRef callback)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            _callback = callback;
            CommandAsync<string>(async _ =>
            {
                await Task.Yield();
                ThrowException();
            });
        }

        protected override void PostRestart(Exception reason)
        {
            _callback.Tell("done");
            base.PostRestart(reason);
        }

        private static void ThrowException()
        {
            throw new Exception("should be handled by supervisor");
        }
    }

    public class AsyncTplActor : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public AsyncTplActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            Command<string>(_ =>
            {
                //this is also safe, all tasks complete in the actor context
                RunTask(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1))
#pragma warning disable AK1005 // Actor context thread safety is being tested here
                        .ContinueWith(_ => { Sender.Tell("done"); });
#pragma warning restore AK1005
                });
            });
        }
    }

    public class AsyncTplExceptionActor : ReceivePersistentActor
    {
        private readonly IActorRef _callback;

        public override string PersistenceId { get; }

        public AsyncTplExceptionActor(string persistenceId, IActorRef callback)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            _callback = callback;
            Command<string>(_ =>
            {
                RunTask(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1))
                   .ContinueWith(_ => { throw new Exception("foo"); });
                });
            });
        }

        protected override void PostRestart(Exception reason)
        {
            _callback.Tell("done");
            base.PostRestart(reason);
        }
    }

    public class RestartMessage
    {
        public object Message { get; private set; }

        public RestartMessage(object message)
        {
            Message = message;
        }
    }

    /// <summary>
    /// Test actor that uses DeferAsync with an async handler inside CommandAsync.
    /// This reproduces the bug where DeferAsync throws "RunTask calls cannot be nested"
    /// when called from within a CommandAsync handler after an await, with no pending persist operations.
    /// See: https://github.com/akkadotnet/akka.net/issues/7998
    /// </summary>
    public sealed class DeferAsyncFromCommandAsyncActor : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public DeferAsyncFromCommandAsyncActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            // This pattern triggers the bug: CommandAsync -> await -> DeferAsync(async handler)
            // with no prior Persist/PersistAsync calls
            CommandAsync<string>(async msg =>
            {
                // Any await causes us to be in a RunTask context
                await Task.Delay(10);

                // DeferAsync with async handler, no pending persist operations
                // BUG: This throws "RunTask calls cannot be nested" because
                // _pendingInvocations.Count == 0 triggers immediate RunTask execution
                DeferAsync(msg, async evt =>
                {
                    await Task.CompletedTask;
                    Sender.Tell("deferred-" + evt);
                });
            });
        }
    }

    /// <summary>
    /// Test actor that uses DeferAsync with a sync handler inside CommandAsync.
    /// This should work correctly (sync handler doesn't use RunTask).
    /// </summary>
    public sealed class DeferAsyncSyncHandlerFromCommandAsyncActor : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public DeferAsyncSyncHandlerFromCommandAsyncActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            CommandAsync<string>(async msg =>
            {
                await Task.Delay(10);

                // DeferAsync with sync handler - this should work
                DeferAsync(msg, evt =>
                {
                    Sender.Tell("deferred-" + evt);
                });
            });
        }
    }

    /// <summary>
    /// Test actor that uses DeferAsync with an async handler after PersistAsync.
    /// This should work because _pendingInvocations.Count > 0.
    /// </summary>
    public sealed class DeferAsyncAfterPersistAsyncActor : ReceivePersistentActor
    {
        public override string PersistenceId { get; }

        public DeferAsyncAfterPersistAsyncActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            RecoverAny(_ => { });

            CommandAsync<string>(async msg =>
            {
                await Task.Delay(10);

                // PersistAsync first - this populates _pendingInvocations
                PersistAsync(msg, evt =>
                {
                    Sender.Tell("persisted-" + evt);
                });

                // DeferAsync with async handler - should queue and work
                DeferAsync(msg, async evt =>
                {
                    await Task.CompletedTask;
                    Sender.Tell("deferred-" + evt);
                });
            });
        }
    }

    public class ReceivePersistentActorAsyncAwaitSpec : AkkaSpec
    {
        public ReceivePersistentActorAsyncAwaitSpec(ITestOutputHelper output = null)
            : base("akka.persistence.journal.plugin = \"akka.persistence.journal.inmem\"", output)
        {
        }

        [Fact]
        public async Task PersistentActors_should_be_able_to_async_await_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create(() => new PersistentAsyncAwaitActor("pid")), "Worker");
            var asker = Sys.ActorOf(Props.Create(() => new PersistentAsker("pid", actor)), "Asker");
            var task = asker.Ask("start");
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_async_await_in_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AsyncAwaitActor("pid")));
            var task = actor.Ask<string>("start", TimeSpan.FromSeconds(50));
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_async_await_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AsyncAwaitActor("pid")), "Worker");
            var asker = Sys.ActorOf(Props.Create(() => new Asker("pid", actor)), "Asker");
            var task = asker.Ask("start");
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public async Task Actors_should_be_able_to_block_ask_message_loop()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AsyncAwaitActor("pid")).WithDispatcher("akka.actor.task-dispatcher"), "Worker");
            var asker = Sys.ActorOf(Props.Create(() => new BlockingAsker("pid", actor)).WithDispatcher("akka.actor.task-dispatcher"), "Asker");
            var task = asker.Ask("start");
            actor.Tell(123, ActorRefs.NoSender);
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact(Skip = "Maybe not possible to solve")]
        public async Task Actors_should_be_able_to_block_ask_self_message_loop()
        {
            var asker = Sys.ActorOf(Props.Create(() => new BlockingAskSelf("pid")), "Asker");
            var task = asker.Ask("start");
            var res = await task;
            Assert.Equal("done", res);
        }

        [Fact]
        public void Actors_should_be_able_to_supervise_async_exceptions()
        {
            var asker = Sys.ActorOf(Props.Create(() => new AsyncExceptionActor("pid", TestActor)));
            asker.Tell("start");
            ExpectMsg("done", TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task Actors_should_be_able_to_use_ContinueWith()
        {
            var asker = Sys.ActorOf(Props.Create(() => new AsyncTplActor("pid")));
            var res = await asker.Ask("start");
            Assert.Equal("done", res);
        }

        [Fact]
        public void Actors_should_be_able_to_supervise_exception_ContinueWith()
        {
            var asker = Sys.ActorOf(Props.Create(() => new AsyncTplExceptionActor("pid", TestActor)));
            asker.Tell("start");
            ExpectMsg("done", TimeSpan.FromSeconds(5));
        }


        [Fact]
        public async Task Actors_should_be_able_to_suspend_reentrancy()
        {
            var asker = Sys.ActorOf(Props.Create(() => new SuspendActor("pid")));
            var res = await asker.Ask<int>("start");
            res.ShouldBe(0);
        }

        [Fact]
        public async Task Actor_should_be_able_to_resume_suspend()
        {
            var asker = Sys.ActorOf(Props.Create(() => new AsyncActor("pid")));

            for (var i = 0; i < 10; i++)
            {
                asker.Tell("msg #" + i);
            }

            var res = await asker.Ask<string>("stop", TimeSpan.FromSeconds(5));
            res.ShouldBe("done");
        }

        [Fact]
        public void Actor_should_be_able_to_ReceiveTimeout_after_async_operation()
        {
            //Given
            var pid = "pa-1";

            var actor = Sys.ActorOf(Props.Create(() => new ReceiveTimeoutAsyncActor(pid)));

            actor.Tell("hello");
            ExpectMsg<string>(m => m == "GotIt");
        }

        public class AsyncExceptionCatcherActor : ReceivePersistentActor
        {
            private string _lastMessage;

            public override string PersistenceId { get; }

            public AsyncExceptionCatcherActor(string persistenceId)
            {
                PersistenceId = persistenceId;

                RecoverAny(_ => { });

                CommandAsync<string>(async m =>
                {
                    _lastMessage = m;
                    try
                    {
                        // Throw an exception in the ActorTaskScheduler
                        await Task.Factory.StartNew(() =>
                        {
                            throw new Exception("should not restart");
                        });
                    }
                    catch (Exception)
                    {
                    }
                });

                Command<int>(_ => Sender.Tell(_lastMessage, Self));
            }
        }

        [Fact]
        public async Task Actor_should_not_restart_if_exception_is_catched()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AsyncExceptionCatcherActor("pid")));

            actor.Tell("hello");

            var lastMessage = await actor.Ask(123);

            lastMessage.ShouldBe("hello");
        }

        public class AsyncFailingActor : ReceivePersistentActor
        {
            public override string PersistenceId { get; }

            public AsyncFailingActor(string persistenceId)
            {
                PersistenceId = persistenceId;

                RecoverAny(_ => { });

                CommandAsync<string>(_ => {
                    ThrowException();
                    return Task.CompletedTask;
                });
            }

            protected override void PreRestart(Exception reason, object message)
            {
                Sender.Tell(new RestartMessage(message), Self);

                base.PreRestart(reason, message);
            }

            private static void ThrowException()
            {
                throw new Exception("foo");
            }
        }

        [Fact]
        public void Actor_PreRestart_should_give_the_failing_message()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AsyncFailingActor("pid")));

            actor.Tell("hello");

            ExpectMsg<RestartMessage>(m => "hello".Equals(m.Message));
        }

        public class AsyncPipeToDelayActor : ReceivePersistentActor
        {
            public override string PersistenceId { get; }

            public AsyncPipeToDelayActor(string persistenceId)
            {
                PersistenceId = persistenceId;

                RecoverAny(_ => { });

                CommandAsync<string>(async msg =>
                {
                    var sender = Sender;
                    var self = Self;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Run(() =>
                    {
                        Thread.Sleep(10);
                        return msg;
                    }).PipeTo(sender, self); 
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                    await Task.Delay(3000);
                });
            }
        }

        public class AsyncReentrantActor : ReceivePersistentActor
        {
            public override string PersistenceId { get; }

            public AsyncReentrantActor(string persistenceId)
            {
                PersistenceId = persistenceId;

                RecoverAny(_ => { });

                CommandAsync<string>(msg => {
                    var sender = Sender;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Run(() =>
                    {
                        //Sleep to make sure the task is not completed when ContinueWith is called
                        Thread.Sleep(100);
                        return msg;
                    }).ContinueWith(_ => sender.Tell(msg)); // ContinueWith will schedule with the implicit ActorTaskScheduler
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                    Thread.Sleep(3000);
                    return Task.CompletedTask;
                });
            }
        }

        [Fact]
        public void ActorTaskScheduler_reentrancy_should_not_be_possible()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AsyncReentrantActor("pid")));
            actor.Tell("hello");

            ExpectNoMsg(1000);
        }

        [Fact(Skip = "Racy - both Tasks run inside the same scheduler with max concurrency == 1")]
        public void Actor_PipeTo_should_not_be_delayed_by_async_receive()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AsyncPipeToDelayActor("pid")));

            actor.Tell("hello");
            ExpectMsg<string>(m => "hello".Equals(m), TimeSpan.FromMilliseconds(1000));
        }

        [Fact]
        public Task Actor_receiveasync_overloads_should_work()
        {
            // Use unique persistence ID to avoid any potential interference from
            // previous test runs or parallel tests sharing static journal state
            var persistenceId = $"async-overloads-{Guid.NewGuid():N}";
            var actor = Sys.ActorOf(Props.Create(() => new AsyncAwaitActor(persistenceId)));

            // Wait for recovery to complete before testing async handlers.
            // Under CI load, recovery can take time and messages sent before recovery
            // completes are stashed, adding variable delay to the first response.
            actor.Tell(AsyncAwaitActor.Ping.Instance);
            ExpectMsg<string>(m => "pong".Equals(m), TimeSpan.FromSeconds(10),
                "Actor should respond to Ping after recovery completes");

            actor.Tell(11);
            ExpectMsg<string>(m => "handled".Equals(m), TimeSpan.FromMilliseconds(1000),
                "Expected 'handled' for int > 10 via CommandAsync<int>");

            actor.Tell(9);
            ExpectMsg<string>(m => "receiveany".Equals(m), TimeSpan.FromMilliseconds(1000),
                "Expected 'receiveany' for int <= 10 via CommandAnyAsync");

            actor.Tell(1.0);
            ExpectMsg<string>(m => "handled".Equals(m), TimeSpan.FromMilliseconds(1000),
                "Expected 'handled' for double via CommandAsync(typeof(double))");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Regression test for https://github.com/akkadotnet/akka.net/issues/7998
        /// DeferAsync with async handler should work when called from CommandAsync,
        /// even without prior Persist/PersistAsync calls.
        /// </summary>
        [Fact(DisplayName = "DeferAsync with async handler should work from CommandAsync without prior persist calls")]
        public async Task DeferAsync_with_async_handler_should_work_from_CommandAsync_without_prior_persist_calls()
        {
            var actor = Sys.ActorOf(Props.Create(() => new DeferAsyncFromCommandAsyncActor("defer-async-pid")));

            actor.Tell("hello");
            var response = await ExpectMsgAsync<string>(TimeSpan.FromSeconds(5));
            response.ShouldBe("deferred-hello");
        }

        /// <summary>
        /// Verify that DeferAsync with sync handler still works from CommandAsync.
        /// </summary>
        [Fact(DisplayName = "DeferAsync with sync handler should work from CommandAsync")]
        public async Task DeferAsync_with_sync_handler_should_work_from_CommandAsync()
        {
            var actor = Sys.ActorOf(Props.Create(() => new DeferAsyncSyncHandlerFromCommandAsyncActor("defer-sync-pid")));

            actor.Tell("world");
            var response = await ExpectMsgAsync<string>(TimeSpan.FromSeconds(5));
            response.ShouldBe("deferred-world");
        }

        /// <summary>
        /// Verify that DeferAsync with async handler works after PersistAsync
        /// (this path was already working since _pendingInvocations.Count > 0).
        /// </summary>
        [Fact(DisplayName = "DeferAsync with async handler should work after PersistAsync")]
        public async Task DeferAsync_with_async_handler_should_work_after_PersistAsync()
        {
            var actor = Sys.ActorOf(Props.Create(() => new DeferAsyncAfterPersistAsyncActor("defer-after-persist-pid")));

            actor.Tell("test");
            // Should receive both persist and defer responses in order
            var first = await ExpectMsgAsync<string>(TimeSpan.FromSeconds(5));
            first.ShouldBe("persisted-test");

            var second = await ExpectMsgAsync<string>(TimeSpan.FromSeconds(5));
            second.ShouldBe("deferred-test");
        }
    }
}

