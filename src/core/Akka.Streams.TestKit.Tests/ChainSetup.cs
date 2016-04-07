using System;
using System.Reactive.Streams;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.TestKit;

namespace Akka.Streams.TestKit.Tests
{
    public class ChainSetup<TIn, TOut, TMat>
    {
        protected readonly TestKitBase System;

        public ChainSetup(
            Func<Flow<TIn, TIn, Unit>, Flow<TIn, TOut, TMat>> stream,
            ActorMaterializerSettings settings,
            ActorMaterializer materializer,
            Func<Source<TOut, Unit>, ActorMaterializer, IPublisher<TOut>> toPublisher,
            TestKitBase system)
        {

            Settings = settings;
            System = system;

            Upstream = TestPublisher.CreateManualProbe<TIn>(system);
            Downstream = TestSubscriber.CreateProbe<TOut>(system);

            var s = Source.FromPublisher<TIn, Unit>(Upstream).Via(stream((Flow<TIn, TIn, Unit>)Flow.Identity<TIn>().Map(x => x).Named("buh")));
            Publisher = toPublisher(s, materializer);
            UpstreamSubscription = Upstream.ExpectSubscription();
            Publisher.Subscribe(Downstream);
            DownstreamSubscription = Downstream.ExpectSubscription();
        }

        public ChainSetup(
            Func<Flow<TIn, TIn, Unit>, Flow<TIn, TOut, TMat>> stream,
            ActorMaterializerSettings settings,
            Func<Source<TOut, Unit>, ActorMaterializer, IPublisher<TOut>> toPublisher,
            TestKitBase system)
            : this(stream, settings, system.Sys.Materializer(), toPublisher, system)
        {
        }

        public ChainSetup(
            Func<Flow<TIn, TIn, Unit>, Flow<TIn, TOut, TMat>> stream,
            ActorMaterializerSettings settings,
            Func<ActorMaterializerSettings, IActorRefFactory, ActorMaterializer> materializerCreator,
            Func<Source<TOut, Unit>, ActorMaterializer, IPublisher<TOut>> toPublisher,
            TestKitBase system)
            : this(stream, settings, materializerCreator(settings, system.Sys), toPublisher, system)
        {
        }

        public ActorMaterializerSettings Settings { get; }
        public TestPublisher.ManualProbe<TIn> Upstream { get; }
        public TestSubscriber.ManualProbe<TOut> Downstream { get; }
        public IPublisher<TOut> Publisher { get; }
        public StreamTestKit.PublisherProbeSubscription<TIn> UpstreamSubscription { get; }
        public ISubscription DownstreamSubscription { get; }
    }
}
