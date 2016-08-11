//-----------------------------------------------------------------------
// <copyright file="ClusterClientMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;
using Contact = Akka.Cluster.Tools.Client.ClusterReceptionist.Contacts;
using GetContacts = Akka.Cluster.Tools.Client.ClusterReceptionist.GetContacts;
using Heartbeat = Akka.Cluster.Tools.Client.ClusterReceptionist.Heartbeat;
using HeartbeatRsp = Akka.Cluster.Tools.Client.ClusterReceptionist.HeartbeatRsp;

namespace Akka.Cluster.Tools.Tests.ClusterClient
{
    public class ClusterClientMessageSerializerSpec : AkkaSpec
    {
        private SerializerWithStringManifest serializer;

        public ClusterClientMessageSerializerSpec() : base(ClusterClientReceptionist.DefaultConfig())
        {
            serializer = new ClusterClientMessageSerializer((ExtendedActorSystem)Sys);
        }

        private void CheckSerialization(object obj)
        {
            var blob = serializer.ToBinary(obj);
            var reference = serializer.FromBinary(blob, serializer.Manifest(obj));
            reference.ShouldBe(obj);
        }

        [Fact]
        public void ClusterClientMessages_must_be_serializable()
        {
            var contactPoints = new List<string>
            {
                "akka.tcp://system@node-1:2552/system/receptionist",
                "akka.tcp://system@node-2:2552/system/receptionist",
                "akka.tcp://system@node-3:2552/system/receptionist"
            };

            CheckSerialization(new Contact(contactPoints.ToImmutableList()));
            CheckSerialization(GetContacts.Instance);
            CheckSerialization(Heartbeat.Instance);
            CheckSerialization(HeartbeatRsp.Instance);
        }
    }
}

