//-----------------------------------------------------------------------
// <copyright file="SystemMessageSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class SystemMessageSerializationSpec : AkkaSpec
    {
        #region actor
        public class Watchee : UntypedActor
        {
            protected override void OnReceive(object message)
            {

            }
        }

        public class Watcher : UntypedActor
        {
            protected override void OnReceive(object message)
            {

            }
        }
        #endregion

        public SystemMessageSerializationSpec() : base(ConfigurationFactory.ParseString("").WithFallback(RemoteConfigFactory.Default()))
        {
        }

        [Fact(Skip = "Not implemented")]
        public void Can_serialize_Create()
        {
            var message = new Create(null);
            AssertEqual(message);
        }

        [Fact(Skip = "Not implemented")]
        public void Can_serialize_CreateWithException()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            var message = new Create(new ActorInitializationException(actorRef, "Failed"));
            var actual = AssertAndReturn(message);
            actual.Failure.Actor.Should().Be(actorRef);
            actual.Failure.Message.Should().Be(message.Failure.Message);
        }

        [Fact(Skip = "Not implemented")]
        public void Can_serialize_Recreate()
        {
            var message = new Recreate(new Exception("test2"));
            AssertAndReturn(message).Cause.Should().Be(message.Cause);
        }

        [Fact(Skip = "Not implemented")]
        public void Can_serialize_Suspend()
        {
            var message = new Suspend();
            AssertAndReturn(message).Should().BeOfType<Suspend>();
        }

        [Fact(Skip = "Not implemented")]
        public void Can_serialize_Resume()
        {
            var message = new Resume(new Exception("test2"));
            AssertAndReturn(message).CausedByFailure.Should().Be(message.CausedByFailure);
        }

        [Fact]
        public void Can_serialize_Terminate()
        {
            var terminate = new Terminate();
            AssertAndReturn(terminate).Should().BeOfType<Terminate>();
        }

        [Fact]
        public void Can_serialize_Supervise()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            var supervise = new Supervise(actorRef, true);
            Supervise deserialized = AssertAndReturn(supervise);
            deserialized.Child.Should().Be(actorRef);
            deserialized.Async.Should().Be(supervise.Async);
        }

        [Fact]
        public void Can_serialize_Watch()
        {
            var watchee = ActorOf<Watchee>().AsInstanceOf<IInternalActorRef>();
            var watcher = ActorOf<Watcher>().AsInstanceOf<IInternalActorRef>();
            var watch = new Watch(watchee, watcher);
            AssertEqual(watch);
        }

        [Fact]
        public void Can_serialize_Unwatch()
        {
            var watchee = ActorOf<Watchee>().AsInstanceOf<IInternalActorRef>();
            var watcher = ActorOf<Watcher>().AsInstanceOf<IInternalActorRef>();
            var unwatch = new Unwatch(watchee, watcher);
            AssertEqual(unwatch);
        }

        [Fact(Skip = "Not implemented")]
        public void Can_serialize_Failed()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            var message = new Failed(actorRef, new Exception("test2"), 435345);
            var actual = AssertAndReturn(message);
            actual.Cause.Should().Be(message.Cause);
            actual.Child.Should().Be(actorRef);
            actual.Uid.Should().Be(message.Uid);
        }

        [Fact]
        public void Can_serialize_DeadwatchNotification()
        {
            var actorRef = ActorOf<BlackHoleActor>();
            var deadwatchNotification = new DeathWatchNotification(actorRef, true, false);
            DeathWatchNotification deserialized = AssertAndReturn(deadwatchNotification);
            deserialized.Actor.Should().Be(actorRef);
            deserialized.AddressTerminated.Should().Be(deadwatchNotification.AddressTerminated);
            deserialized.ExistenceConfirmed.Should().Be(deadwatchNotification.ExistenceConfirmed);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<SystemMessageSerializer>();
            var serializedBytes = serializer.ToBinary(message);
            return (T)serializer.FromBinary(serializedBytes, typeof(T));
        }

        private void AssertEqual<T>(T message)
        {
            var deserialized = AssertAndReturn(message);
            Assert.Equal(message, deserialized);
        }
    }
}