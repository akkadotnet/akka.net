//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Cluster.Tools.PublishSubscribe.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;
using Status = Akka.Cluster.Tools.PublishSubscribe.Internal.Status;
using Delta = Akka.Cluster.Tools.PublishSubscribe.Internal.Delta;
using Bucket = Akka.Cluster.Tools.PublishSubscribe.Internal.Bucket;
using Send = Akka.Cluster.Tools.PublishSubscribe.Send;
using SendToAll = Akka.Cluster.Tools.PublishSubscribe.SendToAll;
using Publish = Akka.Cluster.Tools.PublishSubscribe.Publish;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class DistributedPubSubMessageSerializerSpec : AkkaSpec
    {
        private SerializerWithStringManifest serializer;

        public DistributedPubSubMessageSerializerSpec() : base(DistributedPubSub.DefaultConfig())
        {
            serializer = new DistributedPubSubMessageSerializer((ExtendedActorSystem)Sys);
        }

        private void CheckSerialization(object obj)
        {
            var blob = serializer.ToBinary(obj);
            var reference = serializer.FromBinary(blob, serializer.Manifest(obj));
            reference.ShouldBe(obj);
        }

        [Fact]
        public void DistributedPubSubMessages_must_be_serializable()
        {
            var address1 = new Address("akka.tcp", "system", "some.host.org", 4711);
            var address2 = new Address("akka.tcp", "system", "other.host.org", 4711);
            var address3 = new Address("akka.tcp", "system", "some.host.org", 4712);
            var u1 = Sys.ActorOf(Props.Empty, "u1");
            var u2 = Sys.ActorOf(Props.Empty, "u2");
            var u3 = Sys.ActorOf(Props.Empty, "u3");
            var u4 = Sys.ActorOf(Props.Empty, "u4");

            CheckSerialization(new Status(new Dictionary<Address, long>
            {
                { address1, 3 },
                { address2, 17 },
                { address3, 5 }
            }));

            CheckSerialization(new Delta(new List<Bucket>
            {
                new Bucket(address1, 3, new Dictionary<string, ValueHolder> {
                    { "/user/u1", new ValueHolder(2, u1) },
                    { "/user/u2", new ValueHolder(3, u2) },
                }.ToImmutableDictionary()),
                new Bucket(address2, 17, new Dictionary<string, ValueHolder> {
                    { "/user/u3", new ValueHolder(17, u3) }
                }.ToImmutableDictionary()),
                new Bucket(address3, 5, new Dictionary<string, ValueHolder> {
                    { "/user/u4", new ValueHolder(4, u4) },
                    { "/user/u5", new ValueHolder(5, null) },
                }.ToImmutableDictionary())
            }.ToArray()));

            CheckSerialization(new Send("/user/u3", "hello", true));
            CheckSerialization(new SendToAll("/user/u3", "hello", true));
            CheckSerialization(new Publish("mytopic", "hello"));
        }
    }
}