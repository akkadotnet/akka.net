//-----------------------------------------------------------------------
// <copyright file="PersistAsyncUnstashOrderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests
{
    /// <summary>
    /// Commands stashed while a persist is in flight must run in the order they were sent,
    /// whichever Persist/PersistAll overload the actor uses.
    /// </summary>
    public class PersistAsyncUnstashOrderSpec : PersistenceSpec
    {
        private const int CommandCount = 20;

        public PersistAsyncUnstashOrderSpec(ITestOutputHelper output)
            : base(Configuration("PersistAsyncUnstashOrderSpec", serialization: "off"), output)
        {
        }

        public enum Shape
        {
            PersistAllSyncHandlerSyncCompletion,
            PersistAllSyncHandlerAsyncCompletion,
            PersistAsyncHandler,
            PersistAllAsyncHandlerSyncCompletion,
            PersistAllSyncHandlerCompletedTaskCompletion,
        }

        private sealed record Evt(int N);

        private sealed class PersistingActor : ReceivePersistentActor
        {
            public override string PersistenceId { get; }

            public PersistingActor(string persistenceId, Shape shape)
            {
                PersistenceId = persistenceId;
                Recover<Evt>(_ => { });

                Command<int>(n =>
                {
                    var sender = Sender;
                    var evt = new[] { new Evt(n) };
                    void Reply() => sender.Tell(n);

                    // Task.Yield() would continue inline on the actor's scheduler, so use a real delay:
                    // the returned task is guaranteed to be incomplete when it is awaited.
                    async Task ReplyAfterDelay()
                    {
                        await Task.Delay(10);
                        Reply();
                    }

                    switch (shape)
                    {
                        case Shape.PersistAllSyncHandlerSyncCompletion:
                            PersistAll(evt, _ => { }, Reply);
                            break;
                        case Shape.PersistAllSyncHandlerAsyncCompletion:
                            PersistAll(evt, _ => { }, ReplyAfterDelay);
                            break;
                        case Shape.PersistAsyncHandler:
                            Persist(evt[0], _ => ReplyAfterDelay());
                            break;
                        case Shape.PersistAllAsyncHandlerSyncCompletion:
                            PersistAll(evt, _ => Task.Delay(10), Reply);
                            break;
                        case Shape.PersistAllSyncHandlerCompletedTaskCompletion:
                            PersistAll(evt, _ => { }, () =>
                            {
                                Reply();
                                return Task.CompletedTask;
                            });
                            break;
                    }
                });
            }
        }

        [Theory(DisplayName = "Should_RunStashedCommandsInSendOrder_When_PersistingWithAnyOverload")]
        [InlineData(Shape.PersistAllSyncHandlerSyncCompletion)]       // control
        [InlineData(Shape.PersistAllSyncHandlerAsyncCompletion)]      // expected RED
        [InlineData(Shape.PersistAsyncHandler)]                       // expected RED
        [InlineData(Shape.PersistAllAsyncHandlerSyncCompletion)]      // control
        [InlineData(Shape.PersistAllSyncHandlerCompletedTaskCompletion)] // control
        public async Task Should_RunStashedCommandsInSendOrder_When_PersistingWithAnyOverload(Shape shape)
        {
            var actor = Sys.ActorOf(Props.Create(() => new PersistingActor(Name, shape)));

            // One burst from one sender: the first command starts a persist, the rest are stashed.
            var sent = Enumerable.Range(1, CommandCount).ToArray();
            foreach (var n in sent)
                actor.Tell(n);

            var replies = new int[CommandCount];
            for (var i = 0; i < CommandCount; i++)
                replies[i] = await ExpectMsgAsync<int>(TimeSpan.FromSeconds(10));

            replies.Should().Equal(sent);
        }

        public enum AsyncHandlerPath
        {
            PersistAsyncHandlerUnstashingUserStash,
            PersistAsyncHandlerFromRecoveryCompleted,
        }

        private sealed class AsyncHandlerPathActor : ReceivePersistentActor
        {
            public override string PersistenceId { get; }

            public AsyncHandlerPathActor(string persistenceId, AsyncHandlerPath path)
            {
                PersistenceId = persistenceId;
                var holding = path == AsyncHandlerPath.PersistAsyncHandlerUnstashingUserStash;

                Recover<Evt>(_ => { });
                Recover<RecoveryCompleted>(_ =>
                {
                    // commands that arrive before this handler finishes stay stashed
                    if (path == AsyncHandlerPath.PersistAsyncHandlerFromRecoveryCompleted)
                        Persist(new Evt(0), _ => Task.Delay(10));
                });

                Command<int>(n =>
                {
                    if (holding)
                        Stash.Stash();
                    else
                        Sender.Tell(n);
                });

                // non-stashing persist whose async handler releases the commands held in the user stash
                Command<string>(_ => PersistAsync(new Evt(0), async _ =>
                {
                    await Task.Delay(10);
                    holding = false;
                    Stash.UnstashAll();
                }));
            }
        }

        [Theory(DisplayName = "Should_RunStashedCommandsInSendOrder_When_AsyncHandlerCompletesOutsideCommandHandler")]
        [InlineData(AsyncHandlerPath.PersistAsyncHandlerUnstashingUserStash)]
        [InlineData(AsyncHandlerPath.PersistAsyncHandlerFromRecoveryCompleted)]
        public async Task Should_RunStashedCommandsInSendOrder_When_AsyncHandlerCompletesOutsideCommandHandler(AsyncHandlerPath path)
        {
            var actor = Sys.ActorOf(Props.Create(() => new AsyncHandlerPathActor(Name, path)));

            var sent = Enumerable.Range(1, CommandCount).ToArray();
            foreach (var n in sent)
                actor.Tell(n);
            if (path == AsyncHandlerPath.PersistAsyncHandlerUnstashingUserStash)
                actor.Tell("release");

            var replies = new int[CommandCount];
            for (var i = 0; i < CommandCount; i++)
                replies[i] = await ExpectMsgAsync<int>(TimeSpan.FromSeconds(10));

            replies.Should().Equal(sent);
        }
    }
}
