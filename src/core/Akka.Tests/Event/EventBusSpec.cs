using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Event
{
    /// <summary>
    /// I used <see cref="TestActorEventBus"/> for both specs, since ActorEventBus and EventBus 
    /// are even to each other at the time, spec is written.
    /// </summary>
    internal class TestActorEventBus : ActorEventBus<object, Type>
    {
        protected override bool IsSubClassification(Type parent, Type child)
        {
            return child.IsAssignableFrom(parent);
        }

        protected override void Publish(object evt, ActorRef subscriber)
        {
            subscriber.Tell(evt);
        }

        protected override bool Classify(object evt, Type classifier)
        {
            return evt.GetType().IsAssignableFrom(classifier);
        }

        protected override Type GetClassifier(object @event)
        {
            return @event.GetType();
        }
    }

    internal class TestActorWrapperActor : ActorBase
    {
        private readonly ActorRef _ref;

        public TestActorWrapperActor(ActorRef actorRef)
        {
            _ref = actorRef;
        }

        protected override bool Receive(object message)
        {
            _ref.Forward(message);
            return true;
        }
    }

    internal struct Notification
    {
        public Notification(ActorRef @ref, int payload) : this()
        {
            Ref = @ref;
            Payload = payload;
        }

        public ActorRef Ref { get; set; }
        public int Payload { get; set; }
    }

    public class EventBusSpec : AkkaSpec
    {
        internal ActorEventBus<object, Type> _bus;

        protected object _evt;
        protected Type _classifier;
        protected ActorRef _subscriber;

        public EventBusSpec()
        {
            _bus = new TestActorEventBus();
            _evt = new Notification(testActor, 1);
            _classifier = typeof (Notification);
            _subscriber = testActor;
        }

        [Fact]
        public void EventBus_allow_subscribers()
        {
            _bus.Subscribe(_subscriber, _classifier).ShouldBe(true);
        }

        [Fact]
        public void EventBus_allow_to_unsubscribe_already_existing_subscribers()
        {
            _bus.Subscribe(_subscriber, _classifier).ShouldBe(true);
            _bus.Unsubscribe(_subscriber, _classifier).ShouldBe(true);
        }

        [Fact]
        public void EventBus_not_allow_to_unsubscribe_not_existing_subscribers()
        {
            _bus.Unsubscribe(_subscriber, _classifier).ShouldBe(false);
        }

        [Fact]
        public void EventBus_not_allow_to_subscribe_same_subscriber_to_same_channel_twice()
        {
            _bus.Subscribe(_subscriber, _classifier).ShouldBe(true);
            _bus.Subscribe(_subscriber, _classifier).ShouldBe(false);
            _bus.Unsubscribe(_subscriber, _classifier).ShouldBe(true);
        }

        [Fact]
        public void EventBus_not_allow_to_unsubscribe_same_subscriber_from_the_same_channel_twice()
        {
            _bus.Subscribe(_subscriber, _classifier).ShouldBe(true);
            _bus.Unsubscribe(_subscriber, _classifier).ShouldBe(true);
            _bus.Unsubscribe(_subscriber, _classifier).ShouldBe(false);
        }

        [Fact]
        public void EventBus_allow_to_add_multiple_subscribers()
        {
            const int max = 10;
            IEnumerable<ActorRef> subscribers = Enumerable.Range(0, max).Select(_ => CreateSubscriber(testActor)).ToList();
            foreach (var subscriber in subscribers)
            {
                _bus.Subscribe(subscriber, _classifier).ShouldBe(true);
            }
            foreach (var subscriber in subscribers)
            {
                _bus.Unsubscribe(subscriber, _classifier).ShouldBe(true);
                DisposeSubscriber(subscriber);
            }

        }

        [Fact]
        public void EventBus_allow_publishing_with_empty_subscribers_list()
        {
            _bus.Publish(new object());
        }

        [Fact]
        public void EventBus_publish_to_the_only_subscriber()
        {
            _bus.Subscribe(_subscriber, _classifier);
            _bus.Publish(_evt);
            expectMsg(_evt);
            expectNoMsg(TimeSpan.FromSeconds(1));
            _bus.Unsubscribe(_subscriber);
        }

        [Fact]
        public void EventBus_publish_to_the_only_subscriber_multiple_times()
        {
            _bus.Subscribe(_subscriber, _classifier);
            _bus.Publish(_evt);
            _bus.Publish(_evt);
            _bus.Publish(_evt);

            expectMsg(_evt);
            expectMsg(_evt);
            expectMsg(_evt);

            expectNoMsg(TimeSpan.FromSeconds(1));
            _bus.Unsubscribe(_subscriber, _classifier);
        }

        [Fact]
        public void EventBus_not_publish_event_to_unindented_subscribers()
        {
            var otherSubscriber = CreateSubscriber(testActor);
            var otherClassifier = typeof (int);

            _bus.Subscribe(_subscriber, _classifier);
            _bus.Subscribe(otherSubscriber, otherClassifier);
            _bus.Publish(_evt);

            expectMsg(_evt);

            _bus.Unsubscribe(_subscriber, _classifier);
            _bus.Unsubscribe(otherSubscriber, otherClassifier);
            expectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void EventBus_not_publish_event_to_former_subscriber()
        {
            _bus.Subscribe(_subscriber, _classifier);
            _bus.Unsubscribe(_subscriber, _classifier);
            _bus.Publish(_evt);
            expectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void EventBus_cleanup_subscribers()
        {
            DisposeSubscriber(_subscriber);
        }

        protected ActorRef CreateSubscriber(ActorRef actor)
        {
            return sys.ActorOf(Props.Create(() => new TestActorWrapperActor(actor)));
        }

        protected void DisposeSubscriber(ActorRef subscriber)
        {
            sys.Stop(subscriber);
        }
    }
}
