//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Singleton;
using Akka.Cluster.Singleton.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests.Singleton
{
    public class ClusterSingletonMessageSerializerSpec : AkkaSpec
    {
        private SerializerWithStringManifest serializer;

        public ClusterSingletonMessageSerializerSpec() : base(ClusterSingletonManager.DefaultConfig())
        {
            serializer = new ClusterSingletonMessageSerializer((ExtendedActorSystem)Sys);
        }

        private void CheckSerialization(object obj)
        {
            var blob = serializer.ToBinary(obj);
            var reference = serializer.FromBinary(blob, serializer.Manifest(obj));
            reference.ShouldBe(obj);
        }

        [Fact]
        public void ClusterSingletonMessages_must_be_serializable()
        {
            CheckSerialization(HandOverDone.Instance);
            CheckSerialization(HandOverInProgress.Instance);
            CheckSerialization(HandOverToMe.Instance);
            CheckSerialization(TakeOverFromMe.Instance);
        }
    }
}