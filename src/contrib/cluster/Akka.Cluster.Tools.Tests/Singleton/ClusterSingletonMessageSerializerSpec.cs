using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Cluster.Tools.Singleton.Serialization;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
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