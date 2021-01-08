//-----------------------------------------------------------------------
// <copyright file="ChainSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Reactive.Streams;

namespace Akka.Streams.TestKit.Tests
{
    public class ChainSetup<TIn, TOut, TMat>
    {
        protected readonly TestKitBase System;

        public ChainSetup(
            Func<Flow<TIn, TIn, NotUsed>, Flow<TIn, TOut, TMat>> stream,
            ActorMaterializerSettings settings,
            ActorMaterializer materializer,
            Func<Source<TOut, NotUsed>, ActorMaterializer, IPublisher<TOut>> toPublisher,
            TestKitBase system)
        {

            Settings = settings;
            System = system;

            Upstream = system.CreateManualPublisherProbe<TIn>();
            Downstream = system.CreateSubscriberProbe<TOut>();

            var s = Source.FromPublisher(Upstream).Via(stream(Flow.Identity<TIn>().Select(x => x).Named("buh")));
            Publisher = toPublisher(s, materializer);
            UpstreamSubscription = Upstream.ExpectSubscription();
            Publisher.Subscribe(Downstream);
            DownstreamSubscription = Downstream.ExpectSubscription();
        }

        public ChainSetup(
            Func<Flow<TIn, TIn, NotUsed>, Flow<TIn, TOut, TMat>> stream,
            ActorMaterializerSettings settings,
            Func<Source<TOut, NotUsed>, ActorMaterializer, IPublisher<TOut>> toPublisher,
            TestKitBase system)
            : this(stream, settings, system.Sys.Materializer(), toPublisher, system)
        {
        }

        public ChainSetup(
            Func<Flow<TIn, TIn, NotUsed>, Flow<TIn, TOut, TMat>> stream,
            ActorMaterializerSettings settings,
            Func<ActorMaterializerSettings, IActorRefFactory, ActorMaterializer> materializerCreator,
            Func<Source<TOut, NotUsed>, ActorMaterializer, IPublisher<TOut>> toPublisher,
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
