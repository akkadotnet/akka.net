//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubDeadLetterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    [Collection(nameof(DistributedPubSubDeadLetterSpec))]
    public class DistributedPubSubDeadLetterSpec : AkkaSpec
    {
        public DistributedPubSubDeadLetterSpec() : base(GetConfig())
        {
        }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString(
                @"akka.actor.provider = cluster"
                + "\nakka.loglevel = INFO"
                + "\nakka.log-dead-letters = on"
                + "\nakka.cluster.pub-sub.send-to-dead-letters-when-no-subscribers = on");
        }
        
        [Fact]
        public async Task DistributedPubSubMediator_should_send_specialized_dead_letter_message_when_no_subscribers()
        {
            // arrange
            var mediator = DistributedPubSub.Get(Sys).Mediator;
            var testMessage = "test-message";

            // act - publish to a topic that no one is subscribed to
            await EventFilter.DeadLetter().ExpectAsync(1, () =>
            {
                mediator.Tell(new Publish("unused-topic", testMessage));
                return Task.CompletedTask;
            });
        }
    }
}