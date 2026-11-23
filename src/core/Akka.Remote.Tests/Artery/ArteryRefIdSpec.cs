//-----------------------------------------------------------------------
// <copyright file="ArteryRefIdSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// End-to-end delivery between Artery systems over loopback TCP when envelopes carry sender and
    /// recipient ref ids: who receives each message and with which <c>Sender</c>.
    /// </summary>
    public class ArteryRefIdSpec : AkkaSpec
    {
        public ArteryRefIdSpec(ITestOutputHelper output) : base(Config(1), output)
        {
        }

        private static Config Config(int inboundLanes) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = {{inboundLanes}}
            """);

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(m => Sender.Tell(m));
        }

        /// <summary>Replies with the path of the sender it saw, then tells that sender "reply".</summary>
        private sealed class SenderReporter : ReceiveActor
        {
            public SenderReporter() => ReceiveAny(_ =>
            {
                Sender.Tell(Sender.Path.ToString());
                Sender.Tell("reply");
            });
        }

        private sealed class Forwarder : ReceiveActor
        {
            public Forwarder(IActorRef target) => ReceiveAny(m => target.Forward(m));
        }

        private sealed class DelayedEcho : ReceiveActor
        {
            public DelayedEcho() => ReceiveAny(m =>
                Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(500), Sender, m, Self));
        }

        /// <summary>Introduces itself to a collector on start, then answers "pong".</summary>
        private sealed class Greeter : ReceiveActor
        {
            public Greeter(ActorSelection collector)
            {
                ReceiveAny(_ => Sender.Tell("pong"));
                collector.Tell("hello");
            }
        }

        private static Address AddressOf(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;

        private static ActorSelection SelectionOf(ActorSystem from, ActorSystem on, string name) =>
            from.ActorSelection(new RootActorPath(AddressOf(on)) / "user" / name);

        private static Task<IActorRef> Resolve(ActorSystem from, ActorSystem on, string name) =>
            SelectionOf(from, on, name).ResolveOne(Timeout);

        private async Task WithSystems(int count, int inboundLanes, Func<ActorSystem[], Task> body)
        {
            var systems = Enumerable.Range(0, count)
                .Select(i => ActorSystem.Create($"refid-{i}", Config(inboundLanes)))
                .ToArray();
            try
            {
                await body(systems);
            }
            finally
            {
                await Task.WhenAll(systems.Select(s => s.Terminate()));
            }
        }

        [Theory(DisplayName = "Ask round trips between two systems complete, one at a time and concurrently")]
        [InlineData(1)]
        [InlineData(4)]
        public Task Should_complete_Ask_round_trips(int inboundLanes) => WithSystems(2, inboundLanes, async s =>
        {
            s[1].ActorOf(Props.Create(() => new Echo()), "echo");
            var echo = await Resolve(s[0], s[1], "echo");

            for (var i = 0; i < 200; i++)
                (await echo.Ask<string>($"seq-{i}", Timeout)).Should().Be($"seq-{i}");

            await Task.WhenAll(Enumerable.Range(0, 64).Select(async w =>
            {
                for (var i = 0; i < 50; i++)
                    (await echo.Ask<string>($"par-{w}-{i}", Timeout)).Should().Be($"par-{w}-{i}");
            }));
        });

        [Fact(DisplayName = "A Tell arrives with the sender's path, and a reply to that sender is delivered")]
        public Task Should_deliver_Tell_with_correct_Sender() => WithSystems(2, 1, async s =>
        {
            s[1].ActorOf(Props.Create(() => new SenderReporter()), "reporter");
            var reporter = await Resolve(s[0], s[1], "reporter");
            var probe = CreateTestProbe(s[0]);
            var expected = probe.Ref.Path.ToStringWithAddress(AddressOf(s[0]));

            for (var i = 0; i < 3; i++)
            {
                reporter.Tell("who", probe.Ref);
                (await probe.ExpectMsgAsync<string>(Timeout)).Should().Be(expected);
                await probe.ExpectMsgAsync("reply", Timeout);
                probe.LastSender.Should().Be(reporter);
            }
        });

        [Fact(DisplayName = "A reply to an Ask that already timed out or was cancelled goes to dead letters")]
        public Task Should_dead_letter_late_Ask_reply() => WithSystems(2, 1, async s =>
        {
            s[1].ActorOf(Props.Create(() => new DelayedEcho()), "delayed");
            var delayed = await Resolve(s[0], s[1], "delayed");
            var deadLetters = CreateTestProbe(s[0]);
            s[0].EventStream.Subscribe(deadLetters.Ref, typeof(DeadLetter));

            await Assert.ThrowsAsync<AskTimeoutException>(() => delayed.Ask<string>("late-timeout", TimeSpan.FromMilliseconds(100)));
            await deadLetters.FishForMessageAsync(m => m is DeadLetter { Message: "late-timeout" }, Timeout);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayed.Ask<string>("late-cancel", null, cts.Token));
            await deadLetters.FishForMessageAsync(m => m is DeadLetter { Message: "late-cancel" }, Timeout);
        });

        [Fact(DisplayName = "After an actor is stopped and re-created at the same path, its old ref dead-letters and its new ref receives")]
        public Task Should_dead_letter_to_old_ref_after_recreation() => WithSystems(2, 1, async s =>
        {
            var original = s[1].ActorOf(Props.Create(() => new Echo()), "target");
            var oldRef = await Resolve(s[0], s[1], "target");
            var probe = CreateTestProbe(s[0]);
            oldRef.Tell("before", probe.Ref);
            await probe.ExpectMsgAsync("before", Timeout);

            var deadLetters = CreateTestProbe(s[1]);
            s[1].EventStream.Subscribe(deadLetters.Ref, typeof(DeadLetter));
            var watcher = CreateTestProbe(s[1]);
            watcher.Watch(original);
            s[1].Stop(original);
            await watcher.ExpectTerminatedAsync(original, Timeout);
            await AwaitAssertAsync(() => s[1].ActorOf(Props.Create(() => new Echo()), "target"), Timeout);

            oldRef.Tell("to-old", probe.Ref);
            await deadLetters.FishForMessageAsync(m => m is DeadLetter { Message: "to-old" }, Timeout);

            var newRef = await Resolve(s[0], s[1], "target");
            newRef.Should().NotBe(oldRef);
            newRef.Tell("to-new", probe.Ref);
            await probe.ExpectMsgAsync("to-new", Timeout);
        });

        [Fact(DisplayName = "A recipient addressed by ActorSelection receives, and its reply is delivered")]
        public Task Should_deliver_to_ActorSelection() => WithSystems(2, 1, async s =>
        {
            s[1].ActorOf(Props.Create(() => new Echo()), "echo");
            var selection = SelectionOf(s[0], s[1], "echo");
            var probe = CreateTestProbe(s[0]);

            for (var i = 0; i < 3; i++)
            {
                selection.Tell($"sel-{i}", probe.Ref);
                await probe.ExpectMsgAsync($"sel-{i}", Timeout);
            }
        });

        [Fact(DisplayName = "More distinct recipients than the ref cache holds all receive their messages")]
        public Task Should_deliver_to_more_recipients_than_cache_capacity() => WithSystems(2, 1, async s =>
        {
            const int count = 5000;
            const int batch = 500;
            var collector = CreateTestProbe(s[0]);
            s[0].ActorOf(Props.Create(() => new Forwarder(collector.Ref)), "collector");
            var collectorSelection = SelectionOf(s[1], s[0], "collector");

            // Each greeter's introduction gives us a ref to it.
            var greeters = new List<IActorRef>(count);
            for (var b = 0; b < count / batch; b++)
            {
                for (var i = 0; i < batch; i++)
                    s[1].ActorOf(Props.Create(() => new Greeter(collectorSelection)), $"greeter-{b * batch + i}");
                for (var i = 0; i < batch; i++)
                {
                    await collector.ExpectMsgAsync("hello", Timeout);
                    greeters.Add(collector.LastSender);
                }
            }

            greeters.Distinct().Should().HaveCount(count);

            var replies = CreateTestProbe(s[0]);
            var repliedBy = new HashSet<IActorRef>();
            for (var b = 0; b < count / batch; b++)
            {
                for (var i = 0; i < batch; i++)
                    greeters[b * batch + i].Tell("ping", replies.Ref);
                for (var i = 0; i < batch; i++)
                {
                    await replies.ExpectMsgAsync("pong", Timeout);
                    repliedBy.Add(replies.LastSender);
                }
            }

            repliedBy.Should().BeEquivalentTo(greeters);
        });

        [Fact(DisplayName = "A sender forwarded to a third system is resolved there to the original sender")]
        public Task Should_resolve_forwarded_sender_on_third_system() => WithSystems(3, 1, async s =>
        {
            s[2].ActorOf(Props.Create(() => new SenderReporter()), "reporter");
            var reporter = await Resolve(s[1], s[2], "reporter");
            s[1].ActorOf(Props.Create(() => new Forwarder(reporter)), "forwarder");
            var forwarder = await Resolve(s[0], s[1], "forwarder");
            var probe = CreateTestProbe(s[0]);
            var expected = probe.Ref.Path.ToStringWithAddress(AddressOf(s[0]));

            for (var i = 0; i < 3; i++)
            {
                forwarder.Tell("who", probe.Ref);
                (await probe.ExpectMsgAsync<string>(Timeout)).Should().Be(expected);
                await probe.ExpectMsgAsync("reply", Timeout);
                probe.LastSender.Path.Should().Be(reporter.Path);
            }
        });
    }
}
