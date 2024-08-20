//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Xunit.Abstractions;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class WrappedMessage : RouterEnvelope, IEquatable<WrappedMessage>
    {
        public WrappedMessage(string message) : base(message)
        {
        }

        public bool Equals(WrappedMessage other) => other is not null && other.Message.Equals(Message);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            return obj is WrappedMessage wrapped && Equals(wrapped);
        }

        public override int GetHashCode() => Message != null ? Message.GetHashCode() : 0;
    }

    public class UnwrappedMessage: IEquatable<UnwrappedMessage>
    {
        public string Message { get; }

        public UnwrappedMessage(string message)
        {
            Message = message;
        }

        public bool Equals(UnwrappedMessage other)
        {
            if (ReferenceEquals(this, other)) return true;
            return other is not null && Message.Equals(other.Message);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj)) return true;
            return obj is WrappedMessage wrapped && Equals(wrapped);
        }

        public override int GetHashCode()
        {
            return Message != null ? Message.GetHashCode() : 0;
        }
    }

    public abstract class DistributedPubSubMediatorRouterSpec : AkkaSpec
    {
        private IActorRef mediator;
        private string path;

        public DistributedPubSubMediatorRouterSpec(Config config, ITestOutputHelper output) : base(config, output)
        {
            mediator = DistributedPubSub.Get(Sys).Mediator;
            path = TestActor.Path.ToStringWithoutAddress();
        }

        protected void NonUnwrappingPubSub<T>(T msg)
        {
            Keep_the_RouterEnvelope_when_sending_to_local_logical_path(msg);
            Keep_the_RouterEnvelope_when_sending_to_logical_path(msg);
            Keep_the_RouterEnvelope_when_sending_to_all_actors_on_logical_path(msg);
            Keep_the_RouterEnvelope_when_sending_to_topic(msg);
            Keep_the_RouterEnvelope_when_sending_to_topic_for_group(msg);
            Send_message_to_dead_letters_if_no_recipients_available(msg);
        }

        private void Keep_the_RouterEnvelope_when_sending_to_local_logical_path<T>(T msg)
        {
            mediator.Tell(new Put(TestActor));
            mediator.Tell(new Send(path, msg, localAffinity: true));
            ExpectMsg(msg);
            mediator.Tell(new Remove(path));
        }

        private void Keep_the_RouterEnvelope_when_sending_to_logical_path<T>(T msg)
        {
            mediator.Tell(new Put(TestActor));
            mediator.Tell(new Send(path, msg, localAffinity: false));
            ExpectMsg(msg);
            mediator.Tell(new Remove(path));
        }

        private void Keep_the_RouterEnvelope_when_sending_to_all_actors_on_logical_path<T>(T msg)
        {
            mediator.Tell(new Put(TestActor));
            mediator.Tell(new SendToAll(path, msg));
            ExpectMsg(msg); // SendToAll does not use provided RoutingLogic
            mediator.Tell(new Remove(path));
        }

        private void Keep_the_RouterEnvelope_when_sending_to_topic<T>(T msg)
        {
            mediator.Tell(new Subscribe("topic", TestActor));
            ExpectMsg<SubscribeAck>();

            mediator.Tell(new Publish("topic", msg));
            ExpectMsg(msg);

            mediator.Tell(new Unsubscribe("topic", TestActor));
            ExpectMsg<UnsubscribeAck>();
        }

        private void Keep_the_RouterEnvelope_when_sending_to_topic_for_group<T>(T msg)
        {
            mediator.Tell(new Subscribe("topic", TestActor, "group"));
            ExpectMsg<SubscribeAck>();

            mediator.Tell(new Publish("topic", msg, sendOneMessageToEachGroup: true));
            ExpectMsg(msg);

            mediator.Tell(new Unsubscribe("topic", TestActor));
            ExpectMsg<UnsubscribeAck>();
        }

        private void Send_message_to_dead_letters_if_no_recipients_available<T>(T msg)
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
        public DistributedPubSubMediatorWithRandomRouterSpec(ITestOutputHelper output) : base(DistributedPubSubMediatorRouterConfig.GetConfig("random"), output)
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
        public DistributedPubSubMediatorWithRoundRobinRouterSpec(ITestOutputHelper output) : base(DistributedPubSubMediatorRouterConfig.GetConfig("round-robin"), output)
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
        public DistributedPubSubMediatorWithBroadcastRouterSpec(ITestOutputHelper output) : base(DistributedPubSubMediatorRouterConfig.GetConfig("broadcast"), output)
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
                akka.actor.serialize-messages = on
                akka.remote.dot-netty.tcp.port = 0
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.pub-sub.routing-logic = {routingLogic}
            ");
        }
    }
}
