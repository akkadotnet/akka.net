using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.IO.Buffers;
using Akka.Pattern;
using Akka.Remote.Serialization;
using Akka.Remote.Transport;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Remote.Tests
{
    public class ExceptionSupportSpec : AkkaSpec
    {
        private readonly ExceptionSupport _serializer;
        private readonly Exception _innerException = new Exception("inner message");
        private readonly Exception _innerException2 = new Exception("inner message 2");

        public ExceptionSupportSpec(ITestOutputHelper output) : base(output)
        {
            _serializer = new ExceptionSupport((ExtendedActorSystem)Sys);
        }

        [Theory]
        [InlineData(new object[]{typeof(ActorInterruptedException)})]
        [InlineData(new object[]{typeof(ActorNotFoundException)})]
        [InlineData(new object[]{typeof(InvalidActorNameException)})]
        [InlineData(new object[]{typeof(LoggerInitializationException)})]
        [InlineData(new object[]{typeof(StashOverflowException)})]
        [InlineData(new object[]{typeof(ConfigurationException)})]
        [InlineData(new object[]{typeof(RejectedExecutionException)})]
        [InlineData(new object[]{typeof(IllegalStateException)})]
        [InlineData(new object[]{typeof(RemoteTransportException)})]
        [InlineData(new object[]{typeof(AkkaProtocolException)})]
        [InlineData(new object[]{typeof(InvalidAssociationException)})]
        public void ExceptionSupport_should_serialize_exceptions_with_inner_exception(Type type)
        {
            var instance = (Exception) Activator.CreateInstance(type, "TestMessage", _innerException);
            AssertDefaultsEquals(instance);
        }

        [Theory]
        [InlineData(new object[]{typeof(ActorKilledException)})]
        [InlineData(new object[]{typeof(AskTimeoutException)})]
        [InlineData(new object[]{typeof(IllegalActorNameException)})]
        [InlineData(new object[]{typeof(IllegalActorStateException)})]
        [InlineData(new object[]{typeof(InvalidMessageException)})]
        [InlineData(new object[]{typeof(SchedulerException)})]
        [InlineData(new object[]{typeof(BufferPoolAllocationException)})]
        public void ExceptionSupport_should_serialize_exceptions_with_message(Type type)
        {
            var instance = (Exception) Activator.CreateInstance(type, "TestMessage");
            AssertDefaultsEquals(instance);
        }

        [Theory]
        [InlineData(new object[]{typeof(UserCalledFailException)})]
        public void ExceptionSupport_should_serialize_exceptions(Type type)
        {
            var instance = (Exception) Activator.CreateInstance(type, new object[]{});
            AssertDefaultsEquals(instance);
        }

        [Fact]
        public void ExceptionSupport_should_serialize_ActorInitializationException()
        {
            var probe = CreateTestProbe();
            var exception = AssertDefaultsEquals(new ActorInitializationException(probe.Ref, "TestMessage", _innerException));

            exception.Actor.Should().NotBeNull();
            exception.Actor.Equals(probe).Should().BeTrue();
        }

        [Fact]
        public void ExceptionSupport_should_serialize_DeathPactException()
        {
            var probe = CreateTestProbe();
            var exception = AssertDefaultsEquals(new DeathPactException(probe.Ref));

            exception.DeadActor.Should().NotBeNull();
            exception.DeadActor.Equals(probe).Should().BeTrue();
        }

        [Fact]
        public void ExceptionSupport_should_serialize_PostRestartException()
        {
            var probe = CreateTestProbe();
            var exception = AssertDefaultsEquals(new PostRestartException(probe.Ref, _innerException, _innerException2));

            exception.Actor.Should().NotBeNull();
            exception.Actor.Equals(probe).Should().BeTrue();
            AssertExceptionEquals(_innerException2, exception.OriginalCause);
        }

        [Fact]
        public void ExceptionSupport_should_serialize_PreRestartException()
        {
            var probe = CreateTestProbe();
            var testMessage = new
            {
                value = 1
            };
            var exception = AssertDefaultsEquals(new PreRestartException(probe.Ref, _innerException2, _innerException, testMessage));

            exception.Actor.Should().NotBeNull();
            exception.Actor.Equals(probe).Should().BeTrue();
            AssertExceptionEquals(_innerException2, exception.RestartException);
            exception.OptionalMessage.Should().BeEquivalentTo(testMessage);
        }

        [Fact]
        public void ExceptionSupport_should_serialize_OpenCircuitException()
        {
            var remaining = new TimeSpan(1234567);
            var exception = AssertDefaultsEquals(new OpenCircuitException("TestMessage", _innerException, remaining));

            exception.RemainingDuration.Should().Be(remaining);
        }

        private T AssertDefaultsEquals<T>(T expected) where T: Exception
        {
            var serialized = _serializer.ExceptionToProto(expected);
            var deserialized = (T)_serializer.ExceptionFromProto(serialized);

            AssertExceptionEquals(expected, deserialized);

            return deserialized;
        }

        private void AssertExceptionEquals(Exception expected, Exception actual)
        {
            actual.Message.Should().Be(expected.Message);
            // HResult is not serialized
            // actual.HResult.Should().Be(expected.HResult);
            actual.Source.Should().Be(expected.Source);
            actual.StackTrace.Should().Be(expected.StackTrace);
            actual.TargetSite.Should().BeEquivalentTo(expected.TargetSite);
            if (actual.InnerException != null)
            {
                AssertExceptionEquals(actual.InnerException, expected.InnerException);
            }
        }
    }
}
