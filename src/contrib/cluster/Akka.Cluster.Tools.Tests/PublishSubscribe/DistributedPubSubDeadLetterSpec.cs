//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubDeadLetterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class DistributedPubSubDeadLetterSpec : AkkaSpec
    {
        public DistributedPubSubDeadLetterSpec(ITestOutputHelper output) : base(GetConfig(), output)
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(
                @"akka.actor.provider = cluster"
                + "\nakka.loglevel = INFO"
                + "\nakka.log-dead-letters = on");
        }
        
        [Fact]
        public async Task DistributedPubSubMediator_should_send_specialized_dead_letter_message_when_no_subscribers()
        {
            // arrange
            var mediator = DistributedPubSub.Get(Sys).Mediator;
            var testMessage = "test-message";

            // Ensure DeadLetterListener is active by retrying until it logs a warmup message
            await AwaitAssertAsync(async () =>
            {
                await EventFilter.Info(contains: "was not delivered").ExpectAsync(1, () =>
                {
                    Sys.DeadLetters.Tell("warmup", Nobody.Instance);
                    return Task.CompletedTask;
                });
            }, TimeSpan.FromSeconds(5));

            // act - publish to a topic that no one is subscribed to
            await EventFilter.Info(contains: "DeadLetterWithNoSubscribers")
                .ExpectAsync(1, () =>
            {
                mediator.Tell(new Publish("unused-topic", testMessage));
                return Task.CompletedTask;
            });
        }
    }
}