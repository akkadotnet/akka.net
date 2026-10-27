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
using Akka.Streams.Tests.Serialization;
using FluentAssertions;
using Reactive.Streams;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Wiring and materialization-panic paths of <see cref="MaterializerSession"/>. These used to build
    /// their wrappers with MakeGenericType from the runtime type of the subscriber or publisher; they
    /// now use the element type the wrappers already carry (#8731). Some specs turn
    /// <c>Akka.DynamicTypeLoading</c> off, so the class runs in the non-parallel switch collection.
    /// </summary>
    [Collection(DynamicTypeLoadingCollection.Name)]
    public class MaterializerSessionSpec : Akka.TestKit.Xunit.TestKit
    {
        private const string SwitchName = "Akka.DynamicTypeLoading";

        public MaterializerSessionSpec(ITestOutputHelper output) : base(output: output)
        {
        }

        [Fact(DisplayName = "Should_subscribe_a_plain_ISubscriber_through_the_publisher_When_a_sink_hands_one_over")]
        public async Task Should_subscribe_a_plain_ISubscriber_through_the_publisher_When_a_sink_hands_one_over()
        {
            var publisher = this.CreateManualPublisherProbe<int>();
            var subscriber = this.CreateManualSubscriberProbe<int>();
            var session = new ProbeSession(SourceToSink(), () => subscriber, () => UntypedPublisher.FromTyped(publisher), throwAfter: int.MaxValue);

            session.Materialize();

            var subscription = await publisher.ExpectSubscriptionAsync();
            subscription.Subscriber.Should().BeSameAs(subscriber);
            await subscriber.ExpectSubscriptionAsync();
        }

        [Fact(DisplayName = "Should_fail_subscribers_and_cancel_publishers_When_materialization_panics")]
        public async Task Should_fail_subscribers_and_cancel_publishers_When_materialization_panics()
        {
            var publisher = this.CreateManualPublisherProbe<int>();
            var subscriber = this.CreateManualSubscriberProbe<int>();
            var session = new ProbeSession(SourceToSink(), () => UntypedSubscriber.FromTyped(subscriber),
                () => UntypedPublisher.FromTyped(publisher), throwAfter: 2);

            session.Invoking(s => s.Materialize()).Should().Throw<TestException>().WithMessage("boom");

            // the regular wiring happened before the panic
            (await publisher.ExpectSubscriptionAsync()).Subscriber.Should().BeSameAs(subscriber);
            await subscriber.ExpectSubscriptionAsync();

            // the panic subscribes a CancellingSubscriber<T> to every publisher...
            var cancelling = await publisher.ExpectSubscriptionAsync();
            cancelling.Subscriber.Should().BeOfType<CancellingSubscriber<int>>();
            await cancelling.ExpectCancellationAsync();

            // ...and an ErrorPublisher<T> to every subscriber
            await subscriber.ExpectSubscriptionAsync();
            var error = await subscriber.ExpectErrorAsync();
            error.Should().BeOfType<MaterializerSession.MaterializationPanicException>()
                .Which.InnerException.Should().BeOfType<TestException>();
        }

        [Fact(DisplayName = "Should_rethrow_the_materialization_failure_and_clean_up_the_other_ports_When_failing_a_foreign_subscriber_throws")]
        public async Task Should_rethrow_the_materialization_failure_and_clean_up_the_other_ports_When_failing_a_foreign_subscriber_throws()
        {
            var publisher = this.CreateManualPublisherProbe<int>();
            var foreign = new ForeignSubscriber();
            var session = new ProbeSession(SourceToSink(), () => foreign, () => UntypedPublisher.FromTyped(publisher), throwAfter: 2);

            // with the switch off, failing the foreign subscriber throws NotSupportedException inside the
            // panic handler; the caller must still see the original failure
            WithDynamicTypeLoadingOff(() =>
                session.Invoking(s => s.Materialize()).Should().Throw<TestException>().WithMessage("boom"));

            // the publisher is cleaned up after the subscriber that could not be, so it proves the loop went on
            await publisher.ExpectSubscriptionAsync();
            var cancelling = await publisher.ExpectSubscriptionAsync();
            cancelling.Subscriber.Should().BeOfType<CancellingSubscriber<int>>();
            await cancelling.ExpectCancellationAsync();
        }

        [Fact(DisplayName = "Should_throw_NotSupportedException_naming_the_switch_When_a_foreign_publisher_is_wired_with_dynamic_type_loading_off")]
        public void Should_throw_NotSupportedException_naming_the_switch_When_a_foreign_publisher_is_wired_with_dynamic_type_loading_off()
        {
            var subscriber = this.CreateManualSubscriberProbe<int>();
            var session = new ProbeSession(SourceToSink(), () => subscriber, () => new ForeignPublisher(), throwAfter: int.MaxValue);

            WithDynamicTypeLoadingOff(() =>
                session.Invoking(s => s.Materialize()).Should().Throw<NotSupportedException>()
                    .WithMessage($"*{nameof(ForeignPublisher)}*{SwitchName}*"));
        }

        [Fact(DisplayName = "Should_throw_NotSupportedException_naming_the_switch_When_a_non_generic_port_needs_a_boundary_with_dynamic_type_loading_off")]
        public void Should_throw_NotSupportedException_naming_the_switch_When_a_non_generic_port_needs_a_boundary_with_dynamic_type_loading_off()
        {
            var inlet = new ForeignInlet("in");
            var outlet = new ForeignOutlet("out");

            WithDynamicTypeLoadingOff(() =>
            {
                inlet.Invoking(i => i.CreateBoundarySubscriber(TestActor, null!, 0))
                    .Should().Throw<NotSupportedException>().WithMessage($"*{nameof(ForeignInlet)}*{SwitchName}*");
                outlet.Invoking(o => o.CreateBoundaryPublisher(TestActor, null!, 0, out _))
                    .Should().Throw<NotSupportedException>().WithMessage($"*{nameof(ForeignOutlet)}*{SwitchName}*");
                outlet.Invoking(o => o.CreateActorOutputBoundary(TestActor, null!, 0))
                    .Should().Throw<NotSupportedException>().WithMessage($"*{SwitchName}*");
                outlet.Invoking(o => o.CreateMaterializedValueSource(StreamLayout.Ignore.Instance))
                    .Should().Throw<NotSupportedException>().WithMessage($"*{SwitchName}*");
            });
        }

        private static void WithDynamicTypeLoadingOff(Action action)
        {
            var hadSwitch = AppContext.TryGetSwitch(SwitchName, out var previous);
            AppContext.SetSwitch(SwitchName, false);
            try
            {
                action();
            }
            finally
            {
                AppContext.SetSwitch(SwitchName, !hadSwitch || previous);
            }
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
        /// Assigns a subscriber (an <see cref="IUntypedSubscriber"/>, or a plain one the way a sink module
        /// does) to every inlet and a publisher to every outlet, and throws once <c>throwAfter</c> atomic
        /// modules have been materialized.
        /// </summary>
        private sealed class ProbeSession : MaterializerSession
        {
            private readonly Func<object> _subscriber;
            private readonly Func<IUntypedPublisher> _publisher;
            private readonly int _throwAfter;
            private int _materialized;

            public ProbeSession(IModule module, Func<object> subscriber, Func<IUntypedPublisher> publisher, int throwAfter)
                : base(module, Attributes.None)
            {
                _subscriber = subscriber;
                _publisher = publisher;
                _throwAfter = throwAfter;
            }

            protected override object MaterializeAtomic(AtomicModule atomic, Attributes effectiveAttributes,
                IDictionary<IModule, object> materializedValues)
            {
                foreach (var inPort in atomic.InPorts)
                    AssignPort(inPort, _subscriber());
                foreach (var outPort in atomic.OutPorts)
                    AssignPort(outPort, _publisher());

                if (++_materialized == _throwAfter)
                    throw new TestException("boom");

                return NotUsed.Instance;
            }
        }

        // Port, publisher and subscriber types from outside Akka.Streams: none of them is an
        // Inlet<T>/Outlet<T>/UntypedPublisher/UntypedSubscriber, so they reach the reflective fallback.

        private sealed class ForeignInlet : Inlet
        {
            public ForeignInlet(string name) : base(name) { }
            public override Inlet CarbonCopy() => new ForeignInlet(Name);
        }

        private sealed class ForeignOutlet : Outlet
        {
            public ForeignOutlet(string name) : base(name) { }
            public override Outlet CarbonCopy() => new ForeignOutlet(Name);
        }

        private sealed class ForeignPublisher : IUntypedPublisher
        {
            public void Subscribe(IUntypedSubscriber subscriber) => throw new InvalidOperationException("not expected");
        }

        /// <summary>
        /// Also an <see cref="ISubscriber{T}"/>, so the regular wiring (which unwraps to the typed
        /// subscriber) can connect it before the panic.
        /// </summary>
        private sealed class ForeignSubscriber : IUntypedSubscriber, ISubscriber<int>
        {
            public void OnSubscribe(ISubscription subscription) { }
            public void OnNext(int element) { }
            public void OnNext(object element) { }
            public void OnError(Exception cause) { }
            public void OnComplete() { }
        }
    }
}
