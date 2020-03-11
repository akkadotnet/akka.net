//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    [Collection(nameof(DistributedPubSubMessageSerializerSpec))]
    public class DistributedPubSubMessageSerializerSpec : AkkaSpec
    {
        public DistributedPubSubMessageSerializerSpec()
            : base(ConfigurationFactory.ParseString(@"akka.actor.provider = cluster").WithFallback(DistributedPubSub.DefaultConfig()))
        {
        }

        [Fact]
        public void Can_serialize_Status()
        {
            var address1 = new Address("akka.tcp", "system", "some.host.org", 4711);
            var address2 = new Address("akka.tcp", "system", "other.host.org", 4711);
            var address3 = new Address("akka.tcp", "system", "some.host.org", 4712);

            var message = new Tools.PublishSubscribe.Internal.Status(new Dictionary<Address, long>
            {
                {address1, 3},
                {address2, 17},
                {address3, 5}
            }, true);

            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Delta()
        {
            var address1 = new Address("akka.tcp", "system", "some.host.org", 4711);
            var address2 = new Address("akka.tcp", "system", "other.host.org", 4711);
            var address3 = new Address("akka.tcp", "system", "some.host.org", 4712);
            var u1 = Sys.ActorOf(Props.Empty, "u1");
            var u2 = Sys.ActorOf(Props.Empty, "u2");
            var u3 = Sys.ActorOf(Props.Empty, "u3");
            var u4 = Sys.ActorOf(Props.Empty, "u4");

            // TODO: Delta should accepts IReadOnlyCollection<Bucket> instead of Bucket[]
            // TODO: Bucket should accepts IDictionary<string, ValueHolder> instead of ImmutableDictionary<string, ValueHolder>
            var message = new Delta(new List<Bucket>
            {
                new Bucket(address1, 3, new Dictionary<string, ValueHolder>
                {
                    {"/user/u1", new ValueHolder(2, u1)},
                    {"/user/u2", new ValueHolder(3, u2)},
                }.ToImmutableDictionary()),
                new Bucket(address2, 17, new Dictionary<string, ValueHolder>
                {
                    {"/user/u3", new ValueHolder(17, u3)}
                }.ToImmutableDictionary()),
                new Bucket(address3, 5, new Dictionary<string, ValueHolder>
                {
                    {"/user/u4", new ValueHolder(4, u4)},
                    {"/user/u5", new ValueHolder(5, null)},
                }.ToImmutableDictionary())
            }.ToArray());

            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Send()
        {
            var message = new Send("/user/u3", "hello", true);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_SendToAll()
        {
            var message = new SendToAll("/user/u3", "hello", true);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Publish()
        {
            var message = new Publish("mytopic", "hello");
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_SendToOneSubscriber()
        {
            var message = new SendToOneSubscriber("hello");
            AssertEqual(message);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = (SerializerWithStringManifest)Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            return (T)serializer.FromBinary(serialized, serializer.Manifest(message));
        }

        private void AssertEqual<T>(T message)
        {
            var deserialized = AssertAndReturn(message);
            Assert.Equal(message, deserialized);
        }
    }
}
