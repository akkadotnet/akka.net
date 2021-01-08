//-----------------------------------------------------------------------
// <copyright file="MessageContainerSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class MessageContainerSerializerSpec : AkkaSpec
    {
        public MessageContainerSerializerSpec() : base(RemoteConfigFactory.Default())
        {
        }

        [Fact]
        public void MessageContainerSerializer_must_resolve_serializer_for_ActorSelectionMessage()
        {
            Sys.Serialization.FindSerializerForType(typeof(ActorSelectionMessage))
                .Should()
                .BeOfType<MessageContainerSerializer>();
        }

        [Fact]
        public void MessageContainerSerializer_must_serialize_and_deserialize_ActorSelectionMessage()
        {
            var elements = new List<SelectionPathElement>()
            {
                new SelectChildName("user"),
                new SelectChildName("a"),
                new SelectChildName("b"),
                new SelectParent(),
                new SelectChildPattern("*"),
                new SelectChildName("c")
            };
            var message = new ActorSelectionMessage("hello", elements.ToArray());
            var actual = AssertAndReturn(message);
            actual.Message.Should().Be(message.Message);
            actual.Elements.Should().BeEquivalentTo(elements);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<MessageContainerSerializer>();
            var serializedBytes = serializer.ToBinary(message);
            return (T)serializer.FromBinary(serializedBytes, typeof(T));
        }
    }
}
