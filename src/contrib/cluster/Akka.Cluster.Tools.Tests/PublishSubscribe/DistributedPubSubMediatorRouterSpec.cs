//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Event;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class WrappedMessage : RouterEnvelope
    {
        public WrappedMessage(string message) : base(message)
        {
        }
    }

    public class UnwrappedMessage
    {
        public string Message { get; }

        public UnwrappedMessage(string message)
        {
            Message = message;
        }
    }

    public abstract class DistributedPubSubMediatorRouterSpec : AkkaSpec
    {
        private IActorRef mediator;
        private string path;

        public DistributedPubSubMediatorRouterSpec(Config config) : base(config)
        {
            mediator = DistributedPubSub.Get(Sys).Mediator;
            path = TestActor.Path.ToStringWithoutAddress();
        }

        protected void NonUnwrappingPubSub(object msg)
        {
            Keep_the_RouterEnvelope_when_sending_to_local_logical_path(msg);
            Keep_the_RouterEnvelope_when_sending_to_logical_path(msg);
            Keep_the_RouterEnvelope_when_sending_to_all_actors_on_logical_path(msg);
            Keep_the_RouterEnvelope_when_sending_to_topic(msg);
            Keep_the_RouterEnvelope_when_sending_to_topic_for_group(msg);
            Send_message_to_dead_letters_if_no_recipients_available(msg);
        }

        private void Keep_the_RouterEnvelope_when_sending_to_local_logical_path(object msg)
        {
            mediator.Tell(new Put(TestActor));
            mediator.Tell(new Send(path, msg, localAffinity: true));
            ExpectMsg(msg);
            mediator.Tell(new Remove(path));
        }

        private void Keep_the_RouterEnvelope_when_sending_to_logical_path(object msg)
        {
            mediator.Tell(new Put(TestActor));
            mediator.Tell(new Send(path, msg, localAffinity: false));
            ExpectMsg(msg);
            mediator.Tell(new Remove(path));
        }

        private void Keep_the_RouterEnvelope_when_sending_to_all_actors_on_logical_path(object msg)
        {
            mediator.Tell(new Put(TestActor));
            mediator.Tell(new SendToAll(path, msg));
            ExpectMsg(msg); // SendToAll does not use provided RoutingLogic
            mediator.Tell(new Remove(path));
        }

        private void Keep_the_RouterEnvelope_when_sending_to_topic(object msg)
        {
            mediator.Tell(new Subscribe("topic", TestActor));
            ExpectMsg<SubscribeAck>();

            mediator.Tell(new Publish("topic", msg));
            ExpectMsg(msg);

            mediator.Tell(new Unsubscribe("topic", TestActor));
            ExpectMsg<UnsubscribeAck>();
        }

        private void Keep_the_RouterEnvelope_when_sending_to_topic_for_group(object msg)
        {
            mediator.Tell(new Subscribe("topic", TestActor, "group"));
            ExpectMsg<SubscribeAck>();

            mediator.Tell(new Publish("topic", msg, sendOneMessageToEachGroup: true));
            ExpectMsg(msg);

            mediator.Tell(new Unsubscribe("topic", TestActor));
            ExpectMsg<UnsubscribeAck>();
        }

        private void Send_message_to_dead_letters_if_no_recipients_available(object msg)
        {
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(probe.Ref, typeof(DeadLetter));
            mediator.Tell(new Publish("nowhere", msg, sendOneMessageToEachGroup: true));
            probe.ExpectMsg<DeadLetter>();
            Sys.EventStream.Unsubscribe(probe.Ref, typeof(DeadLetter));
        }
    }

    public class DistributedPubSubMediatorWithRandomRouterSpec : DistributedPubSubMediatorRouterSpec
    {
        public DistributedPubSubMediatorWithRandomRouterSpec() : base(DistributedPubSubMediatorRouterConfig.GetConfig("random"))
        {
        }

        [Fact]
        public void DistributedPubSubMediator_when_sending_wrapped_message()
        {
            var msg = new WrappedMessage("hello");
            NonUnwrappingPubSub(msg);
        }

        [Fact]
        public void DistributedPubSubMediator_when_sending_unwrapped_message()
        {
            var msg = new UnwrappedMessage("hello");
            NonUnwrappingPubSub(msg);
        }
    }

    public class DistributedPubSubMediatorWithRoundRobinRouterSpec : DistributedPubSubMediatorRouterSpec
    {
        public DistributedPubSubMediatorWithRoundRobinRouterSpec() : base(DistributedPubSubMediatorRouterConfig.GetConfig("round-robin"))
        {
        }

        [Fact]
        public void DistributedPubSubMediator_when_sending_wrapped_message()
        {
            var msg = new WrappedMessage("hello");
            NonUnwrappingPubSub(msg);
        }

        [Fact]
        public void DistributedPubSubMediator_when_sending_unwrapped_message()
        {
            var msg = new UnwrappedMessage("hello");
            NonUnwrappingPubSub(msg);
        }
    }

    public class DistributedPubSubMediatorWithBroadcastRouterSpec : DistributedPubSubMediatorRouterSpec
    {
        public DistributedPubSubMediatorWithBroadcastRouterSpec() : base(DistributedPubSubMediatorRouterConfig.GetConfig("broadcast"))
        {
        }

        [Fact]
        public void DistributedPubSubMediator_when_sending_wrapped_message()
        {
            var msg = new WrappedMessage("hello");
            NonUnwrappingPubSub(msg);
        }

        [Fact]
        public void DistributedPubSubMediator_when_sending_unwrapped_message()
        {
            var msg = new UnwrappedMessage("hello");
            NonUnwrappingPubSub(msg);
        }
    }

    public class DistributedPubSubMediatorWithHashRouterSpec : AkkaSpec
    {
        public DistributedPubSubMediatorWithHashRouterSpec() : base(DistributedPubSubMediatorRouterConfig.GetConfig("consistent-hashing"))
        {
        }

        [Fact]
        public void DistributedPubSubMediator_with_Consistent_Hash_router_not_be_allowed_constructed_by_extension()
        {
            Intercept<ArgumentException>(() =>
            {
                var mediator = DistributedPubSub.Get(Sys).Mediator;
            });
        }

        [Fact]
        public void DistributedPubSubMediator_with_Consistent_Hash_router_not_be_allowed_constructed_by_settings()
        {
            Intercept<ArgumentException>(() =>
            {
                var config =
                    DistributedPubSubMediatorRouterConfig.GetConfig("random")
                        .WithFallback(DistributedPubSub.DefaultConfig())
                        .WithFallback(Sys.Settings.Config)
                        .GetConfig("akka.cluster.pub-sub");
                Assert.False(config.IsNullOrEmpty());
                DistributedPubSubSettings.Create(config).WithRoutingLogic(new ConsistentHashingRoutingLogic(Sys));
            });
        }
    }

    public static class DistributedPubSubMediatorRouterConfig
    {
        public static Config GetConfig(string routingLogic)
        {
            return ConfigurationFactory.ParseString($@"
                akka.loglevel = INFO
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.pub-sub.routing-logic = {routingLogic}
            ");
        }
    }
}
