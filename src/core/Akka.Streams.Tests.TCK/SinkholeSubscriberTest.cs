//-----------------------------------------------------------------------
// <copyright file="SinkholeSubscriberTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Implementation;
using Reactive.Streams;
using Reactive.Streams.TCK;

namespace Akka.Streams.Tests.TCK
{
    class SinkholeSubscriberTest : SubscriberWhiteboxVerification<int?>
    {
        public SinkholeSubscriberTest() : base(new TestEnvironment())
        {
        }

        public override int? CreateElement(int element) => element;

        public override ISubscriber<int?> CreateSubscriber(WhiteboxSubscriberProbe<int?> probe)
            => new Subscriber(probe);

        private sealed class Subscriber : ISubscriber<int?>
        {
            private readonly SinkholeSubscriber<int?> _hole = new SinkholeSubscriber<int?>(new TaskCompletionSource<NotUsed>());
            private readonly WhiteboxSubscriberProbe<int?> _probe;

            public Subscriber(WhiteboxSubscriberProbe<int?> probe)
            {
                _probe = probe;
            }

            public void OnSubscribe(ISubscription subscription)
            {
                _probe.RegisterOnSubscribe(new Puppet(subscription));
                _hole.OnSubscribe(subscription);
            }

            private sealed class Puppet : ISubscriberPuppet
            {
                private readonly ISubscription _subscription;

                public Puppet(ISubscription subscription)
                {
                    _subscription = subscription;
                }

                public void TriggerRequest(long elements) => _subscription.Request(elements);

                public void SignalCancel() => _subscription.Cancel();
            }

            public void OnNext(int? element)
            {
                _hole.OnNext(element);
                _probe.RegisterOnNext(element);
            }

            public void OnComplete()
            {
                _hole.OnComplete();
                _probe.RegisterOnComplete();
            }

            public void OnError(Exception cause)
            {
                _hole.OnError(cause);
                _probe.RegisterOnError(cause);
            }
        }
    }
}
