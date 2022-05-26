//-----------------------------------------------------------------------
// <copyright file="ChainSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Reactive.Streams;

namespace Akka.Streams.TestKit
{
    public class ChainSetup<TIn, TOut, TMat>
    {
        private readonly Func<Source<TOut, NotUsed>, ActorMaterializer, IPublisher<TOut>> _toPublisher;
        private readonly Func<Flow<TIn, TIn, NotUsed>, Flow<TIn, TOut, TMat>> _stream;
        private readonly ActorMaterializer _materializer;
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
            _toPublisher = toPublisher;
            _stream = stream;
            _materializer = materializer;
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

        public virtual async Task<ChainSetup<TIn, TOut, TMat>> InitializeAsync()
        {
            _upstream = System.CreateManualPublisherProbe<TIn>();
            _downstream = System.CreateSubscriberProbe<TOut>();

            var s = Source.FromPublisher(_upstream).Via(_stream(Flow.Identity<TIn>().Select(x => x).Named("buh")));
            _publisher = _toPublisher(s, _materializer);
            _upstreamSubscription = await _upstream.ExpectSubscriptionAsync();
            _publisher.Subscribe(_downstream);
            _downstreamSubscription = await _downstream.ExpectSubscriptionAsync();

            Initialized = true;
            
            return this;
        }

        private TestPublisher.ManualProbe<TIn> _upstream;
        private TestSubscriber.ManualProbe<TOut> _downstream;
        private IPublisher<TOut> _publisher;
        private StreamTestKit.PublisherProbeSubscription<TIn> _upstreamSubscription;
        private ISubscription _downstreamSubscription;

        public bool Initialized { get; private set; }

        public ActorMaterializerSettings Settings { get; }

        public ActorMaterializer Materializer => _materializer;
        public TestPublisher.ManualProbe<TIn> Upstream
        {
            get
            {
                EnsureInitialized();
                return _upstream;
            }
        }

        public TestSubscriber.ManualProbe<TOut> Downstream
        {
            get
            {
                EnsureInitialized();
                return _downstream;
            }
        }

        public IPublisher<TOut> Publisher
        {
            get
            {
                EnsureInitialized();
                return _publisher;
            }
        }

        public StreamTestKit.PublisherProbeSubscription<TIn> UpstreamSubscription
        {
            get
            {
                EnsureInitialized();
                return _upstreamSubscription;
            }
        }

        public ISubscription DownstreamSubscription
        {
            get
            {
                EnsureInitialized();
                return _downstreamSubscription;
            }
        }

        protected virtual void EnsureInitialized()
        {
            if (!Initialized)
                throw new InvalidOperationException(
                    $"ChainSetup has not been initialized. Please make sure to call {nameof(InitializeAsync)} first.");
        }
    }
}
