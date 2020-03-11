//-----------------------------------------------------------------------
// <copyright file="ClusterClientMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.ClusterClient
{
    public class ClusterClientMessageSerializerSpec : AkkaSpec
    {
        public ClusterClientMessageSerializerSpec()
            : base(ConfigurationFactory.ParseString(@"akka.actor.provider = cluster
                                                      akka.remote.dot-netty.tcp.port = 0")
            .WithFallback(ClusterClientReceptionist.DefaultConfig()))
        {
        }

        [Fact]
        public void Can_serialize_Contact()
        {
            var contactPoints = new List<string>
            {
                "akka.tcp://system@node-1:2552/system/receptionist",
                "akka.tcp://system@node-2:2552/system/receptionist",
                "akka.tcp://system@node-3:2552/system/receptionist"
            };

            var message = new ClusterReceptionist.Contacts(contactPoints.ToImmutableList());
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_GetContacts()
        {
            var message = ClusterReceptionist.GetContacts.Instance;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Heartbeat()
        {
            var message = ClusterReceptionist.Heartbeat.Instance;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_HeartbeatRsp()
        {
            var message = ClusterReceptionist.HeartbeatRsp.Instance;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_ReceptionistShutdown()
        {
            var message = ClusterReceptionist.ReceptionistShutdown.Instance;
            AssertEqual(message);
        }

        private T AssertAndReturn<T>(T message)
        {
            var serializer = (ClusterClientMessageSerializer)Sys.Serialization.FindSerializerFor(message);
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

