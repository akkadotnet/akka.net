//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class ProtobufSerializerSpec : AkkaSpec
    {
        public ProtobufSerializerSpec() : base(RemoteConfigFactory.Default())
        {
        }

        [Fact]
        public void Can_serialize_ProtobufMessage()
        {
            var message = new AddressData();
            message.System = "sys";
            message.Hostname = "localhost";
            message.Protocol = "akka.tcp";
            message.Port = 54645;
            AssertEqual(message);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<ProtobufSerializer>();
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
