//-----------------------------------------------------------------------
// <copyright file="JournalInterceptorsSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using System.Threading.Tasks;
    using Akka.Persistence.TestKit;
    using FluentAssertions;
    using Xunit;

    public class JournalInterceptorsSpecs
    {
        [Fact]
        public void noop_immediately_returns_without_exception()
        {
            JournalInterceptors.Noop.Instance
                .Awaiting(x => x.InterceptAsync(null))
                .ShouldNotThrow();
        }

        [Fact]
        public void failure_must_throw_specific_exception()
        {
            JournalInterceptors.Failure.Instance
                .Awaiting(x => x.InterceptAsync(null))
                .ShouldThrowExactly<TestJournalFailureException>();
        }

        [Fact]
        public void rejection_must_throw_specific_exception()
        {
            JournalInterceptors.Rejection.Instance
                .Awaiting(x => x.InterceptAsync(null))
                .ShouldThrowExactly<TestJournalRejectionException>();
        }

        [Fact]
        public async Task delay_must_call_next_interceptor_after_specified_delay()
        {
            var duration = TimeSpan.FromMilliseconds(100);
            var probe = new InterceptorProbe();
            var delay = new JournalInterceptors.Delay(duration, probe);

            var startedAt = DateTime.Now;
            await delay.InterceptAsync(null);

            probe.WasCalled.Should().BeTrue();
            probe.CalledAt.Should().BeOnOrAfter(startedAt + duration);
        }

        [Fact]
        public async Task on_type_must_call_next_interceptor_when_message_is_exactly_awaited_type()
        {
            var probe = new InterceptorProbe();
            var onType = new JournalInterceptors.OnType(typeof(SpecificMessage), probe);
            var message = new Persistent(new SpecificMessage());

            await onType.InterceptAsync(message);

            probe.WasCalled.Should().BeTrue();
            probe.Message.Should().BeSameAs(message);
        }

        [Fact]
        public async Task on_type_must_call_next_interceptor_when_message_is_subclass_of_awaited_type()
        {
            var probe = new InterceptorProbe();
            var onType = new JournalInterceptors.OnType(typeof(SpecificMessage), probe);
            var message = new Persistent(new SubclassMessage());

            await onType.InterceptAsync(message);

            probe.WasCalled.Should().BeTrue();
            probe.Message.Should().BeSameAs(message);
        }

        [Fact]
        public async Task on_type_must_call_next_interceptor_when_message_is_implements_awaited_interface_type()
        {
            var probe = new InterceptorProbe();
            var onType = new JournalInterceptors.OnType(typeof(IMessageWithInterface), probe);
            var message = new Persistent(new MessageWithInterface());

            await onType.InterceptAsync(message);

            probe.WasCalled.Should().BeTrue();
            probe.Message.Should().BeSameAs(message);
        }

        [Fact]
        public async Task on_type_must_not_call_next_interceptor_when_message_does_not_correspond_to_described_rules()
        {
            var probe = new InterceptorProbe();
            var onType = new JournalInterceptors.OnType(typeof(SubclassMessage), probe);
            var message = new Persistent(new SpecificMessage());

            await onType.InterceptAsync(message);

            probe.WasCalled.Should().BeFalse();
        }

        [Fact]
        public async Task on_condition_must_accept_sync_lambda()
        {
            var probe = new InterceptorProbe();
            var onCondition = new JournalInterceptors.OnCondition(_ => true, probe);

            await onCondition.InterceptAsync(null);

            probe.WasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task on_condition_must_accept_async_lambda()
        {
            var probe = new InterceptorProbe();
            var onCondition = new JournalInterceptors.OnCondition(_ => Task.FromResult(true), probe);

            await onCondition.InterceptAsync(null);

            probe.WasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task on_condition_must_call_next_interceptor_unless_predicate_returns_false()
        {
            var probe = new InterceptorProbe();
            var onCondition = new JournalInterceptors.OnCondition(_ => false, probe);

            await onCondition.InterceptAsync(null);

            probe.WasCalled.Should().BeFalse();
        }

        [Fact]
        public async Task on_condition_with_negation_must_call_next_interceptor_unless_predicate_returns_true()
        {
            var probe = new InterceptorProbe();
            var onCondition = new JournalInterceptors.OnCondition(_ => false, probe, negate: true);

            await onCondition.InterceptAsync(null);

            probe.WasCalled.Should().BeTrue();
        }

        [Fact]
        public async Task on_condition_must_pass_the_same_message_to_predicate()
        {
            var probe = new InterceptorProbe();
            var expectedMessage = new Persistent("test");
            
            var onCondition = new JournalInterceptors.OnCondition(message =>
            {
                message.Should().BeSameAs(expectedMessage);
                return false;
            }, probe);

            await onCondition.InterceptAsync(expectedMessage);
        }


        private class SpecificMessage  { }

        private class SubclassMessage : SpecificMessage { }

        private interface IMessageWithInterface { }

        private class MessageWithInterface : IMessageWithInterface { }

        private class InterceptorProbe : IJournalInterceptor
        {
            public bool WasCalled { get; private set; }
            public DateTime CalledAt { get; private set; }
            public IPersistentRepresentation Message { get; private set; }

            public Task InterceptAsync(IPersistentRepresentation message)
            {
                CalledAt = DateTime.Now;
                WasCalled = true;
                Message = message;

                return Task.CompletedTask;
            }
        }
    }
}
