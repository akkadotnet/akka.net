//-----------------------------------------------------------------------
// <copyright file="MaterializerSessionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Wiring and materialization-panic paths of <see cref="MaterializerSession"/>. These used to build
    /// their wrappers with MakeGenericType from the runtime type of the subscriber or publisher; they
    /// now use the element type the wrappers already carry (#8731).
    /// </summary>
    public class MaterializerSessionSpec : Akka.TestKit.Xunit.TestKit
    {
        public MaterializerSessionSpec(ITestOutputHelper output) : base(output: output)
        {
        }

        [Fact(DisplayName = "Should_subscribe_a_plain_ISubscriber_through_the_publisher_When_a_sink_hands_one_over")]
        public async Task Should_subscribe_a_plain_ISubscriber_through_the_publisher_When_a_sink_hands_one_over()
        {
            var publisher = this.CreateManualPublisherProbe<int>();
            var subscriber = this.CreateManualSubscriberProbe<int>();
            var session = new ProbeSession<int>(SourceToSink(), publisher, subscriber, plainSubscriber: true, throwAfter: int.MaxValue);

            session.Materialize();

            var subscription = await publisher.ExpectSubscriptionAsync();
            subscription.Subscriber.Should().BeSameAs(subscriber);
            await subscriber.ExpectSubscriptionAsync();
        }

        [Fact(DisplayName = "Should_fail_subscribers_and_cancel_publishers_When_materialization_panics_with_a_value_type")]
        public Task Should_fail_subscribers_and_cancel_publishers_When_materialization_panics_with_a_value_type()
            => PanicAsync<int>();

        [Fact(DisplayName = "Should_fail_subscribers_and_cancel_publishers_When_materialization_panics_with_a_reference_type")]
        public Task Should_fail_subscribers_and_cancel_publishers_When_materialization_panics_with_a_reference_type()
            => PanicAsync<string>();

        private async Task PanicAsync<T>()
        {
            var publisher = this.CreateManualPublisherProbe<T>();
            var subscriber = this.CreateManualSubscriberProbe<T>();
            var session = new ProbeSession<T>(SourceToSink(), publisher, subscriber, plainSubscriber: false, throwAfter: 2);

            session.Invoking(s => s.Materialize()).Should().Throw<TestException>().WithMessage("boom");

            // the regular wiring happened before the panic
            (await publisher.ExpectSubscriptionAsync()).Subscriber.Should().BeSameAs(subscriber);
            await subscriber.ExpectSubscriptionAsync();

            // the panic subscribes a CancellingSubscriber<T> to every publisher...
            var cancelling = await publisher.ExpectSubscriptionAsync();
            cancelling.Subscriber.Should().BeOfType<CancellingSubscriber<T>>();
            await cancelling.ExpectCancellationAsync();

            // ...and an ErrorPublisher<T> to every subscriber
            await subscriber.ExpectSubscriptionAsync();
            var error = await subscriber.ExpectErrorAsync();
            error.Should().BeOfType<MaterializerSession.MaterializationPanicException>()
                .Which.InnerException.Should().BeOfType<TestException>();
        }

        private static IModule SourceToSink()
        {
            var source = new ProbeModule(inlets: 0, outlets: 1);
            var sink = new ProbeModule(inlets: 1, outlets: 0);
            return source
                .Compose<object, object, object>(sink, Keep.None)
                .Wire(source.OutPorts.First(), sink.InPorts.First());
        }

        private sealed class ProbeModule : AtomicModule
        {
            public ProbeModule(int inlets, int outlets)
            {
                Shape = new AmorphousShape(
                    Enumerable.Range(0, inlets).Select(i => new Inlet<object>("in" + i)).ToImmutableArray<Inlet>(),
                    Enumerable.Range(0, outlets).Select(i => new Outlet<object>("out" + i)).ToImmutableArray<Outlet>());
            }

            public override Shape Shape { get; }
            public override IModule ReplaceShape(Shape shape) => throw new NotSupportedException();
            public override IModule CarbonCopy() => throw new NotSupportedException();
            public override Attributes Attributes => Attributes.None;
            public override IModule WithAttributes(Attributes attributes) => this;
        }

        /// <summary>
        /// Assigns the probes to the ports of each atomic module, the way a source or sink module would,
        /// and throws once <c>throwAfter</c> atomic modules have been materialized.
        /// </summary>
        private sealed class ProbeSession<T> : MaterializerSession
        {
            private readonly TestPublisher.ManualProbe<T> _publisher;
            private readonly TestSubscriber.ManualProbe<T> _subscriber;
            private readonly bool _plainSubscriber;
            private readonly int _throwAfter;
            private int _materialized;

            public ProbeSession(IModule module, TestPublisher.ManualProbe<T> publisher, TestSubscriber.ManualProbe<T> subscriber,
                bool plainSubscriber, int throwAfter) : base(module, Attributes.None)
            {
                _publisher = publisher;
                _subscriber = subscriber;
                _plainSubscriber = plainSubscriber;
                _throwAfter = throwAfter;
            }

            protected override object MaterializeAtomic(AtomicModule atomic, Attributes effectiveAttributes,
                IDictionary<IModule, object> materializedValues)
            {
                foreach (var inPort in atomic.InPorts)
                    AssignPort(inPort, _plainSubscriber ? _subscriber : UntypedSubscriber.FromTyped(_subscriber));
                foreach (var outPort in atomic.OutPorts)
                    AssignPort(outPort, UntypedPublisher.FromTyped(_publisher));

                if (++_materialized == _throwAfter)
                    throw new TestException("boom");

                return NotUsed.Instance;
            }
        }
    }
}
