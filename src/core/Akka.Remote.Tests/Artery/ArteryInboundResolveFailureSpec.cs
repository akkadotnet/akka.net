//-----------------------------------------------------------------------
// <copyright file="ArteryInboundResolveFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// An inbound Artery message whose sender or recipient path cannot be resolved is a failure of that one
    /// message: it is dropped with a warning, and the connection and the messages around it are unaffected.
    /// With one inbound lane the dispatch runs inline in the connection's sink, so an exception there would
    /// fail the stream. <c>Sys</c> is the receiving system; a second system sends the malformed messages.
    /// </summary>
    public sealed class ArteryInboundResolveFailureSpec : AkkaSpec
    {
        private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(5);
        private const int Around = 20;

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = 1
            """);

        public ArteryInboundResolveFailureSpec(ITestOutputHelper output) : base(ArteryConfig(), output)
        {
        }

        private Address ReceiverAddress => RARP.For(Sys).Provider.DefaultAddress;

        private async Task WithSenderSystemAsync(Func<ActorSystem, Task> test)
        {
            var sender = ActorSystem.Create("InboundResolveSender", ArteryConfig());
            try
            {
                await test(sender);
            }
            finally
            {
                await sender.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        /// <summary>A ref whose serialized path has a uid fragment that cannot be parsed.</summary>
        private sealed class MalformedRef : MinimalActorRef
        {
            public MalformedRef(Address address, IActorRefProvider provider)
            {
                Path = new ChildActorPath(new RootActorPath(address), "bad#uid", 0);
                Provider = provider;
            }

            public override ActorPath Path { get; }

            public override IActorRefProvider Provider { get; }
        }

        private sealed class Forward : ReceiveActor
        {
            public Forward(IActorRef target)
            {
                ReceiveAny(msg => target.Tell(msg));
            }
        }

        private static RemoteActorRef RemoteRefOn(ActorSystem from, ActorPath path)
        {
            var provider = (RemoteActorRefProvider)((ExtendedActorSystem)from).Provider;
            return new RemoteActorRef(provider.Transport, RARP.For(from).Provider.DefaultAddress, path,
                ActorRefs.Nobody, Props.None, Deploy.None);
        }

        private ActorPath MalformedRecipientPath() =>
            new ChildActorPath(new RootActorPath(ReceiverAddress), "bad#uid", 0);

        private async Task<IActorRef> ForwardToTestActor(ActorSystem sender, string name)
        {
            Sys.ActorOf(Props.Create(() => new Forward(TestActor)), name);
            return await sender.ActorSelection($"{ReceiverAddress}/user/{name}").ResolveOne(10.Seconds());
        }

        /// <summary>Sends a burst with one bad message in the middle, then expects every good message in order.</summary>
        private async Task BurstWithOneBadMessage(string expectedWarning, Func<ActorSystem, IActorRef, Action<object>> badSend)
        {
            await WithSenderSystemAsync(async sender =>
            {
                var forward = await ForwardToTestActor(sender, "forward");

                // Establish the association first, so the burst below runs on a connection that exists.
                forward.Tell("warmup");
                await ExpectMsgAsync("warmup", 10.Seconds());

                var sendBad = badSend(sender, forward);

                await EventFilter.Warning(contains: expectedWarning).ExpectOneAsync(async () =>
                {
                    for (var i = 0; i < Around; i++)
                        forward.Tell($"m-{i}");

                    sendBad("bad");

                    for (var i = Around; i < 2 * Around; i++)
                        forward.Tell($"m-{i}");

                    for (var i = 0; i < 2 * Around; i++)
                        await ExpectMsgAsync($"m-{i}", Prompt);
                });

                // The connection is still usable, and nothing was delivered for the bad message.
                forward.Tell("after");
                await ExpectMsgAsync("after", Prompt);
            });
        }

        [Fact(DisplayName = "Should_KeepConnectionAndDeliverOtherMessages_When_SenderPathIsMalformed")]
        public Task Should_KeepConnectionAndDeliverOtherMessages_When_SenderPathIsMalformed() =>
            BurstWithOneBadMessage("cannot resolve sender path", (sender, forward) =>
            {
                var malformed = new MalformedRef(RARP.For(sender).Provider.DefaultAddress, ((ExtendedActorSystem)sender).Provider);
                return msg => forward.Tell(msg, malformed);
            });

        [Fact(DisplayName = "Should_KeepConnectionAndDeliverOtherMessages_When_RecipientPathIsMalformed")]
        public Task Should_KeepConnectionAndDeliverOtherMessages_When_RecipientPathIsMalformed() =>
            BurstWithOneBadMessage("cannot resolve recipient path", (sender, _) =>
            {
                var recipient = RemoteRefOn(sender, MalformedRecipientPath());
                return msg => recipient.Tell(msg);
            });

        [Fact(DisplayName = "Should_KeepDeathWatchWorking_When_SystemMessageRecipientPathIsMalformed")]
        public async Task Should_KeepDeathWatchWorking_When_SystemMessageRecipientPathIsMalformed()
        {
            await WithSenderSystemAsync(async sender =>
            {
                var forward = await ForwardToTestActor(sender, "forward");
                var target = Sys.ActorOf(Props.Create(() => new Forward(TestActor)), "target");
                var remoteTarget = await sender.ActorSelection($"{ReceiverAddress}/user/target").ResolveOne(10.Seconds());
                var watcher = CreateTestProbe(sender);

                // A system message that deserializes fine but is addressed to an unresolvable recipient.
                var badRecipient = RemoteRefOn(sender, MalformedRecipientPath());
                await EventFilter.Warning(contains: nameof(DeathWatchNotification)).ExpectOneAsync(() =>
                {
                    badRecipient.SendSystemMessage(new DeathWatchNotification(watcher.Ref, true, false));
                    return Task.CompletedTask;
                });

                // The control connection and its system-message sequence are intact: a Watch is
                // delivered and acknowledged, and stopping the target yields Terminated.
                await watcher.WatchAsync(remoteTarget);
                Sys.Stop(target);
                await watcher.ExpectMsgAsync<Terminated>(10.Seconds());

                forward.Tell("after");
                await ExpectMsgAsync("after", Prompt);
            });
        }

        [Fact(DisplayName = "Should_DeadLetterLaterMessages_When_BufferedRemoteDeployedMessageHasMalformedSender")]
        public async Task Should_DeadLetterLaterMessages_When_BufferedRemoteDeployedMessageHasMalformedSender()
        {
            await WithSenderSystemAsync(async sender =>
            {
                // A /remote path that does not exist: its messages are buffered while the retries run.
                var senderAddress = RARP.For(sender).Provider.DefaultAddress;
                var path = ActorPath.Parse($"{ReceiverAddress}/remote/akka/{senderAddress.System}@{senderAddress.Host}:{senderAddress.Port}/user/missing");
                var recipient = RemoteRefOn(sender, path);
                var malformed = new MalformedRef(senderAddress, ((ExtendedActorSystem)sender).Provider);
                var goodSender = CreateTestProbe(sender).Ref;
                Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));

                // Both arrive inside the retry window: the first must not take the second down with it.
                await EventFilter.Warning(contains: "cannot resolve sender path").ExpectOneAsync(async () =>
                {
                    recipient.Tell("bad", malformed);
                    recipient.Tell("good", goodSender);

                    var deadLetter = await FishForMessageAsync<DeadLetter>(d => "good".Equals(d.Message), 10.Seconds());
                    deadLetter.Message.Should().Be("good");
                });
            });
        }
    }
}
