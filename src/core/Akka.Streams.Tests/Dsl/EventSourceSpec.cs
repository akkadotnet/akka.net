//-----------------------------------------------------------------------
// <copyright file="EventSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class EventSourceSpec : AkkaSpec
    {
        private event EventHandler<int> _event;

        private readonly ActorMaterializer _materializer;
        public EventSourceSpec(ITestOutputHelper helper) : base(helper)
        {
            _materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void EventSource_must_emit_received_event_to_the_stream()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            Source.FromEvent<EventHandler<int>, int>(
                conversion: onNext => (sender, e) => onNext(e),
                addHandler: h => _event += h,
                removeHandler: h => _event -= h)
                .To(Sink.FromSubscriber(s))
                .Run(_materializer);
            var sub = s.ExpectSubscription();

            sub.Request(2);
            _event?.Invoke(this, 1);
            s.ExpectNext(1);
            _event?.Invoke(this, 2);
            s.ExpectNext(2);
            _event?.Invoke(this, 3);
            sub.Request(1);
            s.ExpectNext(3);
            sub.Cancel();
        }

        [Fact]
        public void EventSource_must_work_with_event_handlers()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            Source.FromEvent<int>(h => _event += h, h => _event -= h)
                .To(Sink.FromSubscriber(s))
                .Run(_materializer);
            var sub = s.ExpectSubscription();

            sub.Request(2);
            _event?.Invoke(this, 1);
            s.ExpectNext(1);
            _event?.Invoke(this, 2);
            s.ExpectNext(2);
            _event?.Invoke(this, 3);
            sub.Request(1);
            s.ExpectNext(3);

            sub.Cancel();
        }

        [Fact]
        public void EventSource_must_be_reusable()
        {
            var source = Source.FromEvent<int>(h => _event += h, h => _event -= h);
            var s1 = this.CreateManualSubscriberProbe<int>();
            var s2 = this.CreateManualSubscriberProbe<int>();

            source.To(Sink.FromSubscriber(s1)).Run(_materializer);
            source.To(Sink.FromSubscriber(s2)).Run(_materializer);

            var sub1 = s1.ExpectSubscription();
            sub1.Request(2);
            var sub2 = s2.ExpectSubscription();
            sub2.Request(2);

            _event?.Invoke(this, 123);

            s1.ExpectNext(123);
            s2.ExpectNext(123);
            sub1.Cancel();
            sub2.Cancel();
        }

        [Fact]
        public void EventSource_must_fail_downstream_in_Fail_overflow_mode()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            Source.FromEvent<int>(h => _event += h, h => _event -= h, 1, OverflowStrategy.Fail)
                .To(Sink.FromSubscriber(s))
                .Run(_materializer);

            s.ExpectSubscription();

            _event?.Invoke(this, 1);
            _event?.Invoke(this, 2);

            s.ExpectError();
        }

        [Fact]
        public void EventSource_must_detach_from_event()
        {
            var s = this.CreateManualSubscriberProbe<int>();
            Source.FromEvent<int>(h => _event += h, h => _event -= h)
                .To(Sink.FromSubscriber(s))
                .Run(_materializer);
            var sub = s.ExpectSubscription();
            sub.Request(2);
            
            _event?.Invoke(this, 1);
            s.ExpectNext(1);

            sub.Cancel();

            _event?.Invoke(this, 2);
            s.ExpectNoMsg();
        }
    }
}
