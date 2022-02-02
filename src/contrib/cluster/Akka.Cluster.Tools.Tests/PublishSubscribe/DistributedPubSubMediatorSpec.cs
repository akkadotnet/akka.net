//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Actor.Dsl;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.TestKit;
using Xunit;
using Akka.Event;
using FluentAssertions;
using System.Threading.Tasks;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    [Collection(nameof(DistributedPubSubMediatorSpec))]
    public class DistributedPubSubMediatorSpec : AkkaSpec
    {
        public DistributedPubSubMediatorSpec() : base(GetConfig()) { }

        public static Config GetConfig()
        {
            return ConfigurationFactory.ParseString("akka.actor.provider = \"Akka.Cluster.ClusterActorRefProvider, Akka.Cluster\"");
        }

        /// <summary>
        /// Checks to see if http://stackoverflow.com/questions/41249465/how-to-test-distributedpubsub-with-the-testkit-in-akka-net is resolved
        /// </summary>
        [Fact]
        public void DistributedPubSubMediator_can_be_activated_in_child_actor()
        {
            EventFilter.Exception<NullReferenceException>().Expect(0, () =>
            {
                var actor = Sys.ActorOf((dsl, context) =>
                {
                    IActorRef mediator = null;
                    dsl.OnPreStart = actorContext =>
                    {
                        mediator = DistributedPubSub.Get(actorContext.System).Mediator;
                    };

                    dsl.Receive<string>(s => s.Equals("check"), (s, actorContext) =>
                    {
                        actorContext.Sender.Tell(mediator);
                    });

                }, "childActor");

                actor.Tell("check");
                var a = ExpectMsg<IActorRef>();
                a.ShouldNotBe(ActorRefs.NoSender);
                a.ShouldNotBe(ActorRefs.Nobody);
            });
        }

        [Fact]
        public async Task DistributedPubSubMediator_should_publish_to_deadletters_if_no_active_subscriber()
        {
            var config = ConfigurationFactory.ParseString(@"
akka {
	log-dead-letters = on
	actor {
		provider = cluster
	}
    extensions = [
		""Akka.Cluster.Tools.PublishSubscribe.DistributedPubSubExtensionProvider, Akka.Cluster.Tools""
	]
	remote {
		dot-netty.tcp {

            hostname = localhost
			port = 4055
		}
	}
    cluster {
		seed-nodes = [""akka.tcp://ClusterSys@localhost:4055""]
        pub-sub {
          removed-time-to-live = 1s
          prune-interval = 1s
        }
	}
}
");
            var bootstrap = BootstrapSetup.Create().WithConfig(config);

            var system = ActorSystem.Create("ClusterSys", bootstrap);
            DistributedPubSub.Get(system);
            var deadListener = CreateTestProbe();
            system.EventStream.Subscribe(deadListener.Ref, typeof(DeadLetter));
            _ = system.ActorOf(Subscriber.Prop("akka"), "handler");
            
            var deadLetter = deadListener.ExpectMsg<DeadLetter>(TimeSpan.FromSeconds(30));
            Assert.NotNull(deadLetter); 
            await Task.CompletedTask;
        }
    }
    public class Subscriber : ReceiveActor
    {
        private readonly IActorRef _self;
        public readonly Cluster Cluster = Cluster.Get(Context.System);
        public readonly IActorRef Mediator = DistributedPubSub.Get(Context.System).Mediator;
        private readonly string _topic; 
        public Subscriber(string topic)
        {
            _self = Self;
            _topic = topic;
            Mediator.Tell(new Subscribe(topic, Self)); 
            Receive<UnsubscribeAck>(unsub =>
            {
                var current = Mediator.Ask<CurrentTopics>(GetTopics.Instance).GetAwaiter().GetResult();
                Context.System.Log.Info($"Received updated value for key '{_topic}': {current}");
                Mediator.Tell(new Publish(_topic, MakeRequest.Instance, true));
                Mediator.Tell(new Publish(_topic, MakeRequest.Instance));
            });

            Receive<SubscribeAck>(unsub =>
            {
                var current = Mediator.Ask<CurrentTopics>(GetTopics.Instance).GetAwaiter().GetResult();
                Context.System.Log.Info($"Received updated value for key 'Ebere': {current}");
            });
            Receive<Unsub>(unsub =>
            {
                Mediator.Tell(new Unsubscribe(_topic, _self));
            });
            Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(10), Self, Unsub.Instance, Self);

        }
        protected override void Unhandled(object message)
        {
            base.Unhandled(message);
        }
        public static Props Prop(string topic)
        {
            return Props.Create(() => new Subscriber(topic));
        }
    }
    public sealed class Unsub
    {
        public static Unsub Instance = new Unsub();
    }
    public sealed class MakeRequest
    {
        public static MakeRequest Instance = new MakeRequest();
    }
}
