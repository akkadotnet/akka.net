//-----------------------------------------------------------------------
// <copyright file="StreamTestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit;
using Akka.Actor;
using Akka.Streams.Implementation;
using Reactive.Streams;

namespace Akka.Streams.TestKit
{
    public class StreamTestKit
    {
        public sealed class CompletedSubscription<T> : ISubscription
        {
            public ISubscriber<T> Subscriber { get; }

            public CompletedSubscription(ISubscriber<T> subscriber)
            {
                Subscriber = subscriber;
            }

            public void Request(long n)
            {
                Subscriber.OnComplete();
            }

            public void Cancel()
            {
            }
        }

        public sealed class FailedSubscription<T> : ISubscription
        {
            public ISubscriber<T> Subscriber { get; }
            public Exception Cause { get; }

            public FailedSubscription(ISubscriber<T> subscriber, Exception cause)
            {
                Subscriber = subscriber;
                Cause = cause;
            }

            public void Request(long n)
            {
                Subscriber.OnError(Cause);
            }

            public void Cancel()
            {
            }
        }

        public sealed class PublisherProbeSubscription<T> : ISubscription
        {
            public ISubscriber<T> Subscriber { get; }
            public TestProbe PublisherProbe { get; }

            public PublisherProbeSubscription(ISubscriber<T> subscriber, TestProbe publisherProbe)
            {
                Subscriber = subscriber;
                PublisherProbe = publisherProbe;
            }

            public void Request(long n)
            {
                PublisherProbe.Ref.Tell(new TestPublisher.RequestMore(this, n));
            }

            public void Cancel()
            {
                PublisherProbe.Ref.Tell(new TestPublisher.CancelSubscription(this));
            }

            public void ExpectRequest(long n, CancellationToken cancellationToken = default)
            {
                ExpectRequestAsync(n, cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }

            public async Task ExpectRequestAsync(long n, CancellationToken cancellationToken = default)
            {
                await PublisherProbe.ExpectMsgAsync<TestPublisher.RequestMore>(
                    isMessage: x => x.NrOfElements == n && Equals(x.Subscription, this), 
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            public long ExpectRequest(CancellationToken cancellationToken = default)
            {
                return ExpectRequestAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }

            public async Task<long> ExpectRequestAsync(CancellationToken cancellationToken = default)
            {
                var msg = await PublisherProbe.ExpectMsgAsync<TestPublisher.RequestMore>(
                    isMessage: x => Equals(this, x.Subscription), 
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return msg.NrOfElements;
            }
            
            public void ExpectCancellation(CancellationToken cancellationToken = default)
            {
                ExpectCancellationAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }

            public async Task ExpectCancellationAsync(CancellationToken cancellationToken = default)
            {
                await PublisherProbe.FishForMessageAsync(
                    isMessage: msg =>
                    {
                        return msg switch
                        {
                            TestPublisher.CancelSubscription cancel when Equals(cancel.Subscription, this) => true,
                            TestPublisher.RequestMore more when Equals(more.Subscription, this) => false,
                            _ => false
                        };
                    }, 
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            
            public void SendNext(T element) => Subscriber.OnNext(element);

            public void SendComplete() => Subscriber.OnComplete();

            public void SendError(Exception cause) => Subscriber.OnError(cause);

            public void SendOnSubscribe() => Subscriber.OnSubscribe(this);
        }

        internal sealed class ProbeSource<T> : SourceModule<T, TestPublisher.Probe<T>>
        {
            private readonly TestKitBase _testKit;

            public ProbeSource(TestKitBase testKit, Attributes attributes, SourceShape<T> shape) : base(shape)
            {
                _testKit = testKit;
                Attributes = attributes;
            }

            public override Attributes Attributes { get; }

            public override IModule WithAttributes(Attributes attributes)
            {
                return new ProbeSource<T>(_testKit, attributes, AmendShape(attributes));
            }

            protected override SourceModule<T, TestPublisher.Probe<T>> NewInstance(SourceShape<T> shape)
            {
                return new ProbeSource<T>(_testKit, Attributes, shape);
            }

            public override IPublisher<T> Create(MaterializationContext context, out TestPublisher.Probe<T> materializer)
            {
                materializer = _testKit.CreatePublisherProbe<T>();
                return materializer;
            }
        }

        internal sealed class ProbeSink<T> : SinkModule<T, TestSubscriber.Probe<T>>
        {
            private readonly TestKitBase _testKit;

            public ProbeSink(TestKitBase testKit, Attributes attributes, SinkShape<T> shape) : base(shape)
            {
                _testKit = testKit;
                Attributes = attributes;
            }

            public override Attributes Attributes { get; }

            public override IModule WithAttributes(Attributes attributes)
            {
                return new ProbeSink<T>(_testKit, attributes, AmendShape(attributes));
            }

            protected override SinkModule<T, TestSubscriber.Probe<T>> NewInstance(SinkShape<T> shape)
            {
                return new ProbeSink<T>(_testKit, Attributes, shape);
            }

            public override object Create(MaterializationContext context, out TestSubscriber.Probe<T> materializer)
            {
                materializer = _testKit.CreateSubscriberProbe<T>();
                return materializer;
            }
        }
    }
}
