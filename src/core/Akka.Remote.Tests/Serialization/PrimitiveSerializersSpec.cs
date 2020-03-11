//-----------------------------------------------------------------------
// <copyright file="PrimitiveSerializersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Remote.Configuration;
using Akka.Remote.Serialization;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class PrimitiveSerializersSpec : AkkaSpec
    {
        public PrimitiveSerializersSpec() : base(RemoteConfigFactory.Default())
        {
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MinValue + 1)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MaxValue - 1)]
        public void Can_serialize_Int32(int value)
        {
            AssertEqual(value);
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MinValue + 1L)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MaxValue - 1L)]
        public void Can_serialize_Int64(long value)
        {
            AssertEqual(value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("hello")]
        [InlineData("árvíztűrőütvefúrógép")]
        public void Can_serialize_String(string value)
        {
            AssertEqual(value);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<PrimitiveSerializers>();
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
