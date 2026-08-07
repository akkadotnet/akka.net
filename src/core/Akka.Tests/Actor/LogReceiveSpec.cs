//-----------------------------------------------------------------------
// <copyright file="LogReceiveSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    /// <summary>
    /// Specs for <see cref="ILogReceive"/> — see https://github.com/akkadotnet/akka.net/issues/5929
    /// </summary>
    public class LogReceiveEnabledSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka.loglevel = DEBUG
            akka.actor.debug.receive = on
        ");

        public LogReceiveEnabledSpec(ITestOutputHelper output) : base(Config, output)
        {
        }

        private sealed class LoggingActor : ReceiveActor, ILogReceive
        {
            public LoggingActor()
            {
                Receive<string>(msg =>
                {
                    if (msg == "ping")
                        Sender.Tell("pong");
                    // other strings fall through as unhandled
                });
            }
        }

        private sealed class SilentActor : ReceiveActor
        {
            public SilentActor()
            {
                Receive<string>(msg =>
                {
                    if (msg == "ping")
                        Sender.Tell("pong");
                });
            }
        }

        [Fact]
        public async Task ILogReceive_should_log_handled_messages_when_debug_receive_is_on()
        {
            var actor = Sys.ActorOf(Props.Create(() => new LoggingActor()), "log-receive-handled");

            await EventFilter.Debug(contains: "received handled message ping")
                .ExpectOneAsync(async () =>
                {
                    actor.Tell("ping");
                    await ExpectMsgAsync("pong");
                });
        }

        [Fact]
        public async Task ILogReceive_should_log_unhandled_messages_when_debug_receive_is_on()
        {
            var actor = Sys.ActorOf(Props.Create(() => new LoggingActor()), "log-receive-unhandled");

            await EventFilter.Debug(contains: "received unhandled message unknown")
                .ExpectOneAsync(() =>
                {
                    actor.Tell("unknown");
                    return Task.CompletedTask;
                });
        }

        [Fact]
        public async Task Actor_without_ILogReceive_should_not_log_received_messages()
        {
            var actor = Sys.ActorOf(Props.Create(() => new SilentActor()), "no-log-receive");

            await EventFilter.Debug(contains: "received handled message ping")
                .ExpectAsync(0, async () =>
                {
                    actor.Tell("ping");
                    await ExpectMsgAsync("pong");
                });
        }
    }

    public class LogReceiveDisabledSpec : AkkaSpec
    {
        private static readonly Config Config = ConfigurationFactory.ParseString(@"
            akka.loglevel = DEBUG
            akka.actor.debug.receive = off
        ");

        public LogReceiveDisabledSpec(ITestOutputHelper output) : base(Config, output)
        {
        }

        private sealed class LoggingActor : ReceiveActor, ILogReceive
        {
            public LoggingActor()
            {
                Receive<string>(_ => Sender.Tell("ok"));
            }
        }

        [Fact]
        public async Task ILogReceive_should_not_log_when_debug_receive_is_off()
        {
            var actor = Sys.ActorOf(Props.Create(() => new LoggingActor()), "log-receive-disabled");

            await EventFilter.Debug(contains: "received handled message")
                .ExpectAsync(0, async () =>
                {
                    actor.Tell("hello");
                    await ExpectMsgAsync("ok");
                });
        }
    }
}
