//-----------------------------------------------------------------------
// <copyright file="AkkaIdentityProcessorVerification.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using NUnit.Framework;
using Reactive.Streams;
using Reactive.Streams.TCK;

namespace Akka.Streams.Tests.TCK
{
    [TestFixture]
    abstract class AkkaIdentityProcessorVerification<T> : IdentityProcessorVerification<T>, IDisposable
    {
        protected AkkaIdentityProcessorVerification() : this(false)
        {

        }

        protected AkkaIdentityProcessorVerification(bool writeLineDebug)
            : this(
                new TestEnvironment(Timeouts.DefaultTimeoutMillis,
                    TestEnvironment.EnvironmentDefaultNoSignalsTimeoutMilliseconds(), writeLineDebug),
                Timeouts.PublisherShutdownTimeoutMillis)
        {
        }

        protected AkkaIdentityProcessorVerification(TestEnvironment environment, long publisherShutdownTimeoutMillis)
            : base(environment, publisherShutdownTimeoutMillis)
        {
            System = ActorSystem.Create(GetType().Name, AkkaSpec.AkkaSpecConfig);
            System.EventStream.Publish(new Mute(new ErrorFilter(typeof(Exception), new ContainsString("Test exception"))));

            Materializer = ActorMaterializer.Create(System,
                ActorMaterializerSettings.Create(System).WithInputBuffer(512, 512));
        }

        protected ActorSystem System { get; private set; }

        protected ActorMaterializer Materializer { get; private set; }

        public override bool SkipStochasticTests { get; } = true;

        public override long MaxSupportedSubscribers { get; } = 1;
        
        public void Dispose()
        {
            if (!System.Terminate().Wait(Timeouts.ShutdownTimeout))
                throw new Exception($"Failed to stop {System.Name} within {Timeouts.ShutdownTimeout}");
        }

        public override IPublisher<T> CreateFailedPublisher()
            => TestPublisher.Error<T>(new Exception("Unable to serve subscribers right now!"));

        protected IProcessor<T, T> ProcessorFromSubscriberAndPublisher(ISubscriber<T> subscriber,
            IPublisher<T> publisher) => new Processor<T, T>(subscriber, publisher);

        private sealed class Processor<TIn, TOut> : IProcessor<TIn, TOut>
        {
            private readonly ISubscriber<TIn> _subscriber;
            private readonly IPublisher<TOut> _publisher;

            public Processor(ISubscriber<TIn> subscriber, IPublisher<TOut> publisher)
            {
                _subscriber = subscriber;
                _publisher = publisher;
            }

            public void OnSubscribe(ISubscription subscription) => _subscriber.OnSubscribe(subscription);

            public void OnNext(TIn element) => _subscriber.OnNext(element);

            public void OnError(Exception cause) => _subscriber.OnError(cause);

            public void OnComplete() => _subscriber.OnComplete();

            public void Subscribe(ISubscriber<TOut> subscriber) => _publisher.Subscribe(subscriber);
        }
    }
}
