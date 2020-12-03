//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class DistributedPubSubMediatorSendingToDeadLettersSpec : AkkaSpec
    {
        private IActorRef mediator;

        public DistributedPubSubMediatorSendingToDeadLettersSpec() : base(DistributedPubSubMediatorDeadLettersConfig.GetConfig(true))
        {
            mediator = DistributedPubSub.Get(Sys).Mediator;
        }

        [Fact]
        public void Send_message_to_dead_letters_if_no_recipients_available()
        {
            var msg = new UnwrappedMessage("hello");
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(probe.Ref, typeof(DeadLetter));
            mediator.Tell(new Publish("nowhere", msg, sendOneMessageToEachGroup: true));
            probe.ExpectMsg<DeadLetter>();
            Sys.EventStream.Unsubscribe(probe.Ref, typeof(DeadLetter));
        }
    }

    public class DistributedPubSubMediatorNotSendingToDeadLettersSpec : AkkaSpec
    {
        private IActorRef mediator;

        public DistributedPubSubMediatorNotSendingToDeadLettersSpec() : base(DistributedPubSubMediatorDeadLettersConfig.GetConfig(false))
        {
            mediator = DistributedPubSub.Get(Sys).Mediator;
        }

        [Fact]
        public void Ignore_message_to_dead_letters_if_no_recipients_available()
        {
            var msg = new UnwrappedMessage("hello");
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(probe.Ref, typeof(DeadLetter));
            mediator.Tell(new Publish("nowhere", msg, sendOneMessageToEachGroup: true));
            probe.ExpectNoMsg();
            Sys.EventStream.Unsubscribe(probe.Ref, typeof(DeadLetter));
        }
    }

    public static class DistributedPubSubMediatorDeadLettersConfig
    {
        public static Config GetConfig(bool sendToDeadLettersWhenNoSubscribers)
        {
            var v = sendToDeadLettersWhenNoSubscribers ? "on" : "off";
            return ConfigurationFactory.ParseString($@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.pub-sub.send-to-dead-letters-when-no-subscribers = {v}
            ");
        }
    }
}
